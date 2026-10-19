using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;
using NecroLens.Data;
using NecroLens.Model;
using NecroLens.util;
using static NecroLens.util.DeepDungeonUtil;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace NecroLens.Service;

/**
 * Tracks the progress when inside a DeepDungeon.
 *
 * 事件來源(刻意不解析封包:opcode 每次改版都會變,值不對就整個插件靜默失效、不報錯):
 *   1. FFXIVClientStructs 的 InstanceContentDeepDungeon 結構(進入/離開、樓層、魔陶器數量),每幀輪詢。
 *   2. Dalamud 的 IChatGui 系統訊息 + Lumina LogMessage 資料表(埋藏寶藏、金寶箱內容物),
 *      比對的是資料表列號取出的在地化字串。
 */
public unsafe class DeepDungeonService : IDisposable
{
    private const int PomanderSlotCount = 16;

    /// <summary>
    /// 遊戲的 chat type 只有低 7 位是「訊息類型」,bit 7~10 是來源、bit 11~14 是目標。
    /// Dalamud 的 IChatGui.ChatMessage 轉發的是**未遮罩的原始值**,所以比對類型前必須自己遮。
    /// </summary>
    private const int ChatTypeMask = 0x7F;

    private readonly Configuration conf;
    public readonly Dictionary<int, int> FloorTimes;
    public int CurrentContentId;
    public DeepDungeonContentInfo.DeepDungeonFloorSetInfo? FloorSetInfo;
    public bool Ready;
    public readonly FloorDetails FloorDetails;
    public readonly Dictionary<Pomander, string> PomanderNames;

    /// 「欄位索引 -> DeepDungeonItem 列號」對照,取自 DeepDungeon 資料表的 PomanderSlot 欄位。
    /// 索引即為遊戲 InstanceContentDeepDungeon.UsePomander(slot) 所要的 slot。
    private readonly Dictionary<byte, Pomander[]?> pomanderSlotCache = new();

    /// 依名稱長度遞減排序的魔陶器名稱,供系統訊息內文比對(先長後短,避免前綴誤判)。
    private readonly List<KeyValuePair<Pomander, string>> pomanderNamesByLength;

    // 系統訊息比對用的在地化字尾(取自 LogMessage 資料表列號,語系跟隨客戶端)
    private readonly string[] hoardTails;
    private readonly string[] itemCappedTails;

    // 上一輪輪詢的狀態快照
    private byte lastFloor;
    private readonly byte[] lastPomanderCounts = new byte[PomanderSlotCount];
    private bool unknownContentWarned;

    /// 本次進場已記過「查不到魔陶器欄位」的 DeepDungeonId;查不到的結果不快取、每幀都會重查,靠它避免每幀洗 log。
    private readonly HashSet<byte> slotMissLogged = new();

    public DeepDungeonService()
    {
        FloorTimes = new Dictionary<int, int>();
        Ready = false;
        conf = Config;
        FloorDetails = new FloorDetails();

        PomanderNames = new Dictionary<Pomander, string>();
        foreach (var pomander in DataManager.GetExcelSheet<DeepDungeonItem>(ClientState.ClientLanguage).Skip(1))
        {
            PomanderNames[(Pomander)pomander.RowId] = pomander.Name.ToString();
        }

        pomanderNamesByLength = PomanderNames
                                .Where(p => !string.IsNullOrWhiteSpace(p.Value))
                                .OrderByDescending(p => p.Value.Length)
                                .ToList();

        hoardTails = BuildLogMessageTails(DataIds.LogHoardDiscovered, DataIds.LogHoardObtained,
                                          DataIds.LogHoardObtainedByOther);
        itemCappedTails = BuildLogMessageTails(DataIds.LogItemCappedPotd, DataIds.LogItemCappedEo);

        Framework.Update += OnFrameworkUpdate;
        ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ChatGui.ChatMessage -= OnChatMessage;
    }

    /**
     * 取出 LogMessage 的在地化文字,並只留下最後一個換行之後的部分當作比對字尾。
     * 這些訊息把物品名稱等參數插在中間,只有結尾是固定文字,因此比對字尾最穩。
     */
    private static string[] BuildLogMessageTails(params uint[] rowIds)
    {
        var result = new List<string>();
        var sheet = DataManager.GetExcelSheet<LogMessage>();
        foreach (var rowId in rowIds)
        {
            string? text = null;
            if (sheet.TryGetRow(rowId, out var row))
                text = row.Text.ToString();

            if (string.IsNullOrWhiteSpace(text))
            {
                PluginLog.Warning($"LogMessage {rowId} 取不到文字,對應的系統訊息偵測將失效。");
                continue;
            }

            var tail = text.Split('\n').Last().Trim();
            if (tail.Length == 0)
            {
                PluginLog.Warning($"LogMessage {rowId} 取到的文字沒有可比對的結尾:{text}");
                continue;
            }

            PluginLog.Debug($"LogMessage {rowId} 比對字尾:「{tail}」");
            result.Add(tail);
        }

        return result.ToArray();
    }

    private static InstanceContentDeepDungeon* GetDeepDungeon()
    {
        var eventFramework = EventFramework.Instance();
        return eventFramework == null ? null : eventFramework->GetInstanceContentDeepDungeon();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            var dd = GetDeepDungeon();
            if (dd == null)
            {
                if (Ready) ExitDeepDungeon();
                return;
            }

            var contentId = (int)dd->ContentId;

            // 同一個 instance 內若 ContentId 變了(換梯段),先收掉再重新進入
            if (Ready && contentId != CurrentContentId)
            {
                PluginLog.Information($"ContentId 由 {CurrentContentId} 變為 {contentId},重新初始化追蹤。");
                ExitDeepDungeon();
                return;
            }

            if (!Ready)
            {
                if (!DeepDungeonContentInfo.ContentInfo.TryGetValue(contentId, out var info))
                {
                    if (!unknownContentWarned)
                    {
                        unknownContentWarned = true;
                        PluginLog.Warning(
                            $"偵測到深宮 director,但 ContentId {contentId} 不在已知清單中,本次不啟用追蹤。");
                    }

                    return;
                }

                unknownContentWarned = false;
                EnterDeepDungeon(contentId, info, dd);
            }

            UpdateFloor(dd);
            UpdatePomanders(dd);

            FloorTimes[FloorDetails.CurrentFloor] = FloorDetails.UpdateFloorTime();
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "輪詢深宮狀態時發生例外");
        }
    }

    private void EnterDeepDungeon(int contentId, DeepDungeonContentInfo.DeepDungeonFloorSetInfo info,
                                  InstanceContentDeepDungeon* dd)
    {
        FloorSetInfo = info;
        CurrentContentId = contentId;
        PluginLog.Information(
            $"進入深宮 ContentId {contentId}(DeepDungeonId {dd->DeepDungeonId}、結構回報樓層 {dd->Floor})");

        FloorTimes.Clear();

        MobService.TryReloadIfEmpty();

        for (var i = info.StartFloor; i < info.StartFloor + 10; i++)
            FloorTimes[i] = 0;

        FloorDetails.CurrentFloor = info.StartFloor - 1; // NextFloor() adds 1
        FloorDetails.RespawnTime = info.RespawnTime;
        FloorDetails.FloorTransfer = true;
        FloorDetails.NextFloor();

        // 中途載入外掛、或直接從深層梯段開始時,一律以結構回報的樓層為準。
        // Floor 是絕對樓層(Floor % 10 == 0 即為王層)。
        lastFloor = dd->Floor;
        if (lastFloor > 0)
        {
            if (lastFloor < info.StartFloor || lastFloor > info.StartFloor + 9)
                PluginLog.Warning($"結構回報樓層 {lastFloor} 不在本梯段 {info.StartFloor}-{info.StartFloor + 9} 內。");

            FloorDetails.CurrentFloor = lastFloor;
        }

        FloorDetails.FloorVerified = true;

        SeedPomanders(dd);

        if (Config.AutoOpenOnEnter)
            Plugin.ShowMainWindow();

        Ready = true;
    }

    private void ExitDeepDungeon()
    {
        PluginLog.Information($"ContentID {CurrentContentId} - 離開深宮");

        FloorDetails.DumpFloorObjects(CurrentContentId);

        FloorSetInfo = null;
        FloorDetails.Clear();
        lastFloor = 0;
        Array.Clear(lastPomanderCounts);
        slotMissLogged.Clear();
        Ready = false;
        Plugin.CloseMainWindow();
    }

    /**
     * 換層偵測:結構的 Floor 欄位變化。
     */
    private void UpdateFloor(InstanceContentDeepDungeon* dd)
    {
        // 讀取畫面期間不要把上一層殘留的物件記進本層
        if (PluginService.Condition[ConditionFlag.BetweenAreas] ||
            PluginService.Condition[ConditionFlag.BetweenAreas51])
            FloorDetails.FloorTransfer = true;

        var floor = dd->Floor;
        if (floor == 0 || floor == lastFloor) return;

        PluginLog.Information($"樓層變化:{lastFloor} -> {floor}");
        lastFloor = floor;

        // 先把上一層的資料倒出去(此時 CurrentFloor 仍是舊樓層),再前進
        FloorDetails.DumpFloorObjects(CurrentContentId);
        FloorDetails.FloorObjects.Clear();
        FloorDetails.FloorTransfer = true;
        FloorDetails.NextFloor();
        FloorDetails.CurrentFloor = floor; // 以結構為準
        FloorDetails.FloorVerified = true;
    }

    private void SeedPomanders(InstanceContentDeepDungeon* dd)
    {
        for (var i = 0; i < PomanderSlotCount; i++)
            lastPomanderCounts[i] = dd->Items[i].Count;
    }

    /**
     * 魔陶器使用偵測:結構中該欄位的持有數減少。
     */
    private void UpdatePomanders(InstanceContentDeepDungeon* dd)
    {
        var slots = GetPomanderSlots(dd->DeepDungeonId, dd->Floor);

        for (var i = 0; i < PomanderSlotCount; i++)
        {
            var count = dd->Items[i].Count;
            var previous = lastPomanderCounts[i];
            lastPomanderCounts[i] = count;

            if (count >= previous) continue;

            if (slots == null || slots[i] == default)
            {
                PluginLog.Warning(
                    $"魔陶器欄位 {i} 的持有數由 {previous} 減為 {count},但查不到對應的 DeepDungeonItem" +
                    $"(DeepDungeonId {dd->DeepDungeonId}),本次效果不會被記錄。");
                continue;
            }

            PluginLog.Debug($"使用魔陶器:欄位 {i} -> {slots[i]}");
            FloorDetails.OnPomanderUsed(slots[i]);
        }
    }

    /**
     * DeepDungeon 資料表的 PomanderSlot 欄位就是遊戲端的欄位順序,
     * 用它把「欄位索引」翻譯成 DeepDungeonItem 列號(即 Pomander 列舉值)。
     * 樓層 0(進場當下的暫態值)或查無此列時不快取、不警告,之後讀到的 DeepDungeonId 會重查;
     * 真的缺資料時,魔陶器被使用的那一刻 UpdatePomanders 仍會警告。
     */
    private Pomander[]? GetPomanderSlots(byte deepDungeonId, byte floor)
    {
        if (deepDungeonId == 0) return null;
        if (pomanderSlotCache.TryGetValue(deepDungeonId, out var cached)) return cached;

        Pomander[]? slots = null;
        if (DataManager.GetExcelSheet<Lumina.Excel.Sheets.DeepDungeon>().TryGetRow(deepDungeonId, out var row))
        {
            slots = new Pomander[PomanderSlotCount];
            var limit = Math.Min(PomanderSlotCount, row.PomanderSlot.Count);
            for (var i = 0; i < limit; i++)
                slots[i] = (Pomander)row.PomanderSlot[i].RowId;

            if (slots.All(s => s == default))
            {
                if (floor == 0)
                {
                    LogSlotMiss(deepDungeonId, floor, "沒有任何 PomanderSlot 資料");
                    return null;
                }

                PluginLog.Warning($"DeepDungeon 資料表第 {deepDungeonId} 列沒有任何 PomanderSlot 資料" +
                                  "(本服可能尚未開放此深宮),魔陶器偵測將停用。");
                slots = null;
            }
        }
        else
        {
            LogSlotMiss(deepDungeonId, floor, "不存在");
            return null;
        }

        pomanderSlotCache[deepDungeonId] = slots;
        return slots;
    }

    private void LogSlotMiss(byte deepDungeonId, byte floor, string reason)
    {
        if (slotMissLogged.Add(deepDungeonId))
            PluginLog.Debug($"DeepDungeon 資料表第 {deepDungeonId} 列{reason}(結構回報樓層 {floor}),暫不啟用魔陶器偵測也不快取,之後讀到的 DeepDungeonId 會重查。");
    }

    /**
     * 系統訊息處理。只剩兩件事結構裡拿不到:埋藏的寶藏是否已被挖出,以及金寶箱裡裝的是哪一個魔陶器
     * (「已達上限、放回寶箱」訊息)。比對字串來自 LogMessage 資料表,語系跟隨客戶端。
     * 🔴 類型閘門必須先遮低 7 位,直接比對 XivChatType.SystemMessage 在 API13 是壞的。
     * Dalamud 轉發的是遊戲原始 chat type,高位還帶著來源/目標欄位;深宮的系統訊息一律帶 target=PC 的 0x800
     * (實機是 2105 = 0x800 | 57),與 XivChatType.SystemMessage(57) 永遠不相等 —— 不遮的話這兩個偵測會**全部靜默失效**。
     */
    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message,
                               ref bool isHandled)
    {
        if (!Ready || ((int)type & ChatTypeMask) != (int)XivChatType.SystemMessage) return;

        try
        {
            var text = message.TextValue;
            if (string.IsNullOrEmpty(text)) return;

            if (hoardTails.Any(tail => text.EndsWith(tail, StringComparison.Ordinal)))
            {
                // 只在狀態真的翻轉時寫一行要使用者回報的診斷。
                if (!FloorDetails.HoardFound)
                    PluginLog.Information("NecroLens:系統訊息回報埋藏的寶藏已被發現/取得。");

                FloorDetails.HoardFound = true;
                return;
            }

            if (itemCappedTails.Any(tail => text.EndsWith(tail, StringComparison.Ordinal)))
                TryRecordDoubleChest(text);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "處理系統訊息時發生例外");
        }
    }

    /**
     * 「無法獲得更多的◯◯了,被重新放回了寶箱中」= 這個金寶箱裝的就是 ◯◯。
     * 訊息內文含有魔陶器名稱,名稱同樣取自 DeepDungeonItem 資料表,因此各語系通用。
     */
    private void TryRecordDoubleChest(string text)
    {
        var player = ObjectTable.LocalPlayer;
        if (player == null) return;

        var match = pomanderNamesByLength
            .FirstOrDefault(p => text.Contains(p.Value, StringComparison.Ordinal));

        if (string.IsNullOrEmpty(match.Value))
        {
            // 得瑪希隆等非魔陶器物品也會用同一則訊息,不是錯誤
            PluginLog.Debug($"系統訊息內文找不到魔陶器名稱,略過:{text}");
            return;
        }

        var chest = ObjectTable
                    .Where(o => o.BaseId == DataIds.GoldChest)
                    .FirstOrDefault(o => o.Position.Distance2D(player.Position) <= 4.6f);

        if (chest == null)
        {
            PluginLog.Debug($"偵測到 {match.Value} 被放回寶箱,但附近沒有金寶箱。");
            return;
        }

        // 每個金寶箱最多寫一次(要使用者回報的診斷)。
        if (!FloorDetails.DoubleChests.ContainsKey(chest.EntityId))
            PluginLog.Information($"NecroLens:金寶箱 {chest.EntityId:X} 裝的是 {match.Value}。");

        FloorDetails.DoubleChests[chest.EntityId] = match.Key;
    }

    private bool CheckChestOpenSafe(ESPObject.ESPType type)
    {
        var info = DungeonService.FloorSetInfo;
        var unsafeChest = false;
        if (info != null)
        {
            unsafeChest = (info.MimicChests == DeepDungeonContentInfo.MimicChests.Silver &&
                           type == ESPObject.ESPType.SilverChest) ||
                          (info.MimicChests == DeepDungeonContentInfo.MimicChests.Gold &&
                           type == ESPObject.ESPType.GoldChest);
        }

        return !unsafeChest || (unsafeChest && conf.OpenUnsafeChests);
    }

    /// <summary><paramref name="gameObject"/>＝剛綁定 <paramref name="espObj"/> 的那個當幀物件。</summary>
    internal void TryInteract(ESPObject espObj, Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject)
    {
        var player = ObjectTable.LocalPlayer!;
        if ((player.StatusFlags & StatusFlags.InCombat) == 0 && conf.OpenChests && espObj.IsChest())
        {
            var type = espObj.Type;

            if (!conf.OpenBronzeCoffers && type == ESPObject.ESPType.BronzeChest) return;
            if (!conf.OpenSilverCoffers && type == ESPObject.ESPType.SilverChest) return;
            if (!conf.OpenGoldCoffers && type == ESPObject.ESPType.GoldChest) return;
            if (!conf.OpenHoards && type == ESPObject.ESPType.AccursedHoardCoffer) return;

            // We dont want to kill the player
            if (type == ESPObject.ESPType.SilverChest && player.CurrentHp <= player.MaxHp * 0.77) return;

            if (CheckChestOpenSafe(type) && espObj.Distance() <= espObj.InteractionDistance()
                                         && !FloorDetails.InteractionList.Contains(espObj.EntityId))
            {
                TargetSystem.Instance()->InteractWithObject((GameObject*)gameObject.Address);
                FloorDetails.InteractionList.Add(espObj.EntityId);
            }
        }
    }

    public void TryNearestOpenChest()
    {
        // Checks every object to be a chest and try to open the
        foreach (var obj in ObjectTable)
            if (obj.IsValid())
            {
                var dataId = obj.BaseId;
                if (DataIds.BronzeChestIDs.Contains(dataId) || DataIds.SilverChest == dataId ||
                    DataIds.GoldChest == dataId || DataIds.AccursedHoardCoffer == dataId)
                {
                    var espObj = new ESPObject(obj);
                    if (CheckChestOpenSafe(espObj.Type) && espObj.Distance() <= espObj.InteractionDistance())
                    {
                        TargetSystem.Instance()->InteractWithObject((GameObject*)obj.Address);
                        break;
                    }
                }
            }
    }

    /**
     * /pomander:查出欄位索引(不是資料表列號),呼叫遊戲本身的
     * InstanceContentDeepDungeon.UsePomander(slot) —— 不需要開視窗。
     */
    public void OnPomanderCommand(string pomanderName)
    {
        if (!TryFindPomanderByName(pomanderName, out var pomander)) return;
        if (!IsPomanderUsable(pomander)) return;

        var dd = GetDeepDungeon();
        if (dd == null)
        {
            PrintChatMessage(Strings.Pomander_NotInDeepDungeon);
            return;
        }

        var slots = GetPomanderSlots(dd->DeepDungeonId, dd->Floor);
        var slot = slots == null ? -1 : Array.IndexOf(slots, pomander);
        var displayName = PomanderNames.GetValueOrDefault(pomander, pomander.ToString());

        if (slot < 0)
        {
            PrintChatMessage(string.Format(Strings.Pomander_NotInThisDungeon, displayName));
            return;
        }

        var item = dd->Items[slot];
        if (item.Count == 0)
        {
            PrintChatMessage(string.Format(Strings.Pomander_NoneHeld, displayName));
            return;
        }

        if (!item.IsUsable)
        {
            PrintChatMessage(string.Format(Strings.Pomander_NotUsableNow, displayName));
            return;
        }

        PrintChatMessage(string.Format(Strings.Pomander_Using, displayName));
        PluginLog.Debug($"/pomander:{displayName} -> 欄位 {slot}");
        dd->UsePomander((uint)slot);
    }

    public void TrackFloorObjects(Dalamud.Game.ClientState.Objects.Types.IGameObject gameObject)
    {
        FloorDetails.TrackFloorObjects(gameObject, CurrentContentId);
    }
}
