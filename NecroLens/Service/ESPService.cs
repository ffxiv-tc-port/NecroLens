using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using NecroLens.Model;
using NecroLens.util;
using static NecroLens.util.ESPUtils;

namespace NecroLens.Service;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ESPService : IDisposable
{
    private readonly Configuration conf;

    private readonly List<ESPObject> mapObjects;

    // 兩組輪流重用的 ESPObject：一組掛在 mapObjects 給繪製端，另一組在本 tick 重新綁定。
    // 不在 mapObjects 裡的那組一律 Unbind。ESPObject 只存純量，繪製端每幀用物件表索引重查。只在 framework 執行緒動。
    private readonly List<ESPObject>[] espPools = [new(), new()];
    private int buildPool;
    private bool poolsBound;
    private readonly List<ESPObject> entityList = new();

    public ESPService()
    {
        PluginLog.Debug("ESP Service loading...");

        mapObjects = new List<ESPObject>();
        conf = Config;

        PluginInterface.UiBuilder.Draw += OnUpdate;
        ClientState.TerritoryChanged += OnCleanup;
        Framework.Update += OnTick;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= OnUpdate;
        ClientState.TerritoryChanged -= OnCleanup;
        Framework.Update -= OnTick;
        mapObjects.Clear();
        entityList.Clear();
        foreach (var pool in espPools) pool.Clear();
        PluginLog.Information("ESP Service unloaded");
    }


    /**
     * Clears the drawable GameObjects on MapChange.
     */
    private void OnCleanup(ushort e)
    {
        Monitor.Enter(mapObjects);
        mapObjects.Clear();
        Monitor.Exit(mapObjects);
    }

    /**
     * Main-Drawing method.
     */
    private void OnUpdate()
    {
        try
        {
            if (ShouldDraw())
            {
                if (!Monitor.TryEnter(mapObjects)) return;

                var drawList = ImGui.GetBackgroundDrawList();
                foreach (var gameObject in mapObjects) DrawEspObject(drawList, gameObject);

                Monitor.Exit(mapObjects);
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }

    private bool DoDrawName(ESPObject espObject)
    {
        return espObject.Type switch
        {
            ESPObject.ESPType.Player => false,
            ESPObject.ESPType.Enemy => !espObject.InCombat(),
            ESPObject.ESPType.Mimic => !espObject.InCombat(),
            ESPObject.ESPType.FriendlyEnemy => !espObject.InCombat(),
            ESPObject.ESPType.BronzeChest => conf.ShowBronzeCoffers,
            ESPObject.ESPType.SilverChest => conf.ShowSilverCoffers,
            ESPObject.ESPType.GoldChest => conf.ShowGoldCoffers,
            ESPObject.ESPType.AccursedHoard => conf.ShowHoards && !DungeonService.FloorDetails.HoardFound,
            ESPObject.ESPType.MimicChest => conf.ShowMimicCoffer,
            ESPObject.ESPType.Trap => conf.ShowTraps,
            ESPObject.ESPType.Return => conf.ShowReturn,
            ESPObject.ESPType.Passage => conf.ShowPassage,
            ESPObject.ESPType.Votife => conf.ShowVotife,
            _ => false
        };
    }

    /**
     * Draws every Object for the ESP-Overlay.
     */
    private void DrawEspObject(ImDrawListPtr drawList, ESPObject espObject)
    {
        // 物件已消失或那一格換了別的物件：這一幀不畫，也不去讀它的記憶體。
        if (!espObject.RefreshForDraw()) return;

        var type = espObject.Type;
        var onScreen = GameGui.WorldToScreen(espObject.Position, out var position2D);
        if (onScreen)
        {
            var distance = espObject.Distance();

            if (conf.ShowPlayerDot && type == ESPObject.ESPType.Player)
                DrawPlayerDot(drawList, position2D);

            if (DoDrawName(espObject))
                DrawName(drawList, espObject, position2D);

            if (espObject.Type == ESPObject.ESPType.AccursedHoard && conf.ShowHoards && !DungeonService.FloorDetails.HoardFound)
            {
                var chestRadius = type == ESPObject.ESPType.AccursedHoard ? 2.0f : 1f; // Make Hoards bigger

                if (distance <= 35 && conf.HighlightCoffers)
                    DrawCircleFilled(drawList, espObject, chestRadius, espObject.RenderColor(), 1f);
            }

            if (espObject.IsChest())
            {
                var highlightThisCoffer = type switch
                {
                    ESPObject.ESPType.BronzeChest => conf.ShowBronzeCoffers,
                    ESPObject.ESPType.SilverChest => conf.ShowSilverCoffers,
                    ESPObject.ESPType.GoldChest => conf.ShowGoldCoffers,
                    ESPObject.ESPType.AccursedHoardCoffer => conf.ShowHoards,
                    _ => true
                };

                // 「寶箱高亮」與「可互動範圍圈」分開判斷。
                // 這樣才能把寶箱高亮讓給 PalacePal(它有歷史記錄資料庫)、
                // 同時保留 NecroLens 獨有的可互動距離提示,兩邊不會重複畫同一個圈。
                if (highlightThisCoffer && distance <= 35 && conf.HighlightCoffers)
                    DrawCircleFilled(drawList, espObject, 1f, espObject.RenderColor(), 1f);
                if (distance <= 10 && conf.ShowCofferInteractionRange)
                    DrawInteractionCircle(drawList, espObject, espObject.InteractionDistance());
            }

            if (conf.ShowTraps && type == ESPObject.ESPType.Trap)
                DrawCircleFilled(drawList, espObject, 1.7f, espObject.RenderColor());

            if (conf.ShowMimicCoffer && type == ESPObject.ESPType.MimicChest)
                DrawCircleFilled(drawList, espObject, 1f, espObject.RenderColor());

            if (conf.HighlightPassage && type == ESPObject.ESPType.Passage)
                DrawCircleFilled(drawList, espObject, 2f, espObject.RenderColor());

            if (conf.ShowVotife && type == ESPObject.ESPType.Votife)
                DrawCircleFilled(drawList, espObject, 2f, espObject.RenderColor());
        }

        if (Config.ShowMobViews &&
            (type == ESPObject.ESPType.Enemy || type == ESPObject.ESPType.Mimic) &&
            (BattleNpcSubKind)espObject.SubKind == BattleNpcSubKind.Enemy &&
            !espObject.InCombat())
        {
            if (conf.ShowPatrolArrow && espObject.IsPatrol())
                DrawFacingDirectionArrow(drawList, espObject, Color.Red.ToUint(), 0.6f);

            if (espObject.Distance() <= 50)
            {
                switch (espObject.AggroType())
                {
                    case ESPObject.ESPAggroType.Proximity:
                        DrawCircle(drawList, espObject, espObject.AggroDistance(),
                                   conf.NormalAggroColor, DefaultFilledOpacity);
                        break;
                    case ESPObject.ESPAggroType.Sound:
                        DrawCircle(drawList, espObject, espObject.AggroDistance(),
                                   conf.SoundAggroColor, DefaultFilledOpacity);
                        DrawCircleFilled(drawList, espObject, espObject.HitboxRadius,
                                         conf.SoundAggroColor, DefaultFilledOpacity);
                        break;
                    case ESPObject.ESPAggroType.Sight:
                        DrawConeFromCenterPoint(drawList, espObject, espObject.SightRadian,
                                                espObject.AggroDistance(), conf.NormalAggroColor);
                        break;
                    default:
                        PluginLog.Error(
                            $"Unable to process AggroType {espObject.AggroType().ToString()}");
                        break;
                }
            }
        }
    }

    /**
     * Method returns true if the ESP is Enabled, In valid state and in DeepDungeon
     */
    private bool ShouldDraw()
    {
        return Config.EnableESP &&
               !(Condition[ConditionFlag.LoggingOut] ||
                 Condition[ConditionFlag.BetweenAreas] ||
                 Condition[ConditionFlag.BetweenAreas51]) &&
                 ObjectTable.LocalPlayer != null &&
                 PlayerState.ContentId > 0
                && DeepDungeonUtil.InDeepDungeon;
    }

    /**
     * Not-Drawing Scanner method updating mapObjects every Tick.
     */
    private void OnTick(IFramework framework)
    {
        try
        {
            if (ShouldDraw())
            {
                // 弱點查詢的預算每個 tick 重設一次；IPC 只在這條（framework）執行緒上打。
                VulnerabilityService.BeginTick();

                var pool = espPools[buildPool];
                var used = 0;
                var published = false;
                try
                {
                    foreach (var obj in ObjectTable)
                    {
                        // Ignore every player object
                        if (obj.IsValid() && !IsIgnoredObject(obj))
                        {
                            MobInfo mobInfo = null!;
                            if (obj is IBattleNpc npcObj)
                                MobService.MobInfoDictionary.TryGetValue(npcObj.NameId, out mobInfo!);

                            var espObj = RentEspObject(pool, ref used, obj, mobInfo);

                            if (obj.BaseId == DataIds.GoldChest
                                && DungeonService.FloorDetails.DoubleChests.TryGetValue(obj.EntityId, out var value))
                            {
                                espObj.ContainingPomander = value;
                            }

                            // 只在 framework 執行緒排隊查弱點（每 tick 上限 5 筆），繪製端純讀快取。
                            if (conf.ShowMobVulnerabilities &&
                                espObj.Type is ESPObject.ESPType.Enemy or ESPObject.ESPType.Mimic)
                                VulnerabilityService.Prefetch(espObj.NameId);

                            DungeonService.TryInteract(espObj, obj);

                            entityList.Add(espObj);
                            DungeonService.TrackFloorObjects(obj);
                        }

                        if (ObjectTable.LocalPlayer != null &&
                            ObjectTable.LocalPlayer.EntityId == obj.EntityId)
                            entityList.Add(RentEspObject(pool, ref used, obj, null));
                    }

                    Monitor.Enter(mapObjects);
                    mapObjects.Clear();
                    mapObjects.AddRange(entityList);
                    Monitor.Exit(mapObjects);
                    published = true;
                }
                finally
                {
                    entityList.Clear();
                    if (published)
                    {
                        // 上一 tick 那組剛被換下 mapObjects，下一 tick 輪到它重新綁定。
                        buildPool ^= 1;
                        UnbindPool(espPools[buildPool]);
                        poolsBound = true;
                    }
                    else
                    {
                        // 沒換上 mapObjects：這次綁定的物件沒有人持有，mapObjects 仍是另一組。
                        UnbindPool(pool);
                    }
                }
            }
            else
            {
                // ShouldDraw() 為 false 時也要清空：不在深宮時不留任何上一層的物件。
                Monitor.Enter(mapObjects);
                mapObjects.Clear();
                Monitor.Exit(mapObjects);

                if (poolsBound)
                {
                    UnbindPool(espPools[0]);
                    UnbindPool(espPools[1]);
                    poolsBound = false;
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }

    }

    private static ESPObject RentEspObject(List<ESPObject> pool, ref int used, IGameObject obj, MobInfo? mobInfo)
    {
        ESPObject espObj;
        if (used < pool.Count)
        {
            espObj = pool[used];
            espObj.Bind(obj, mobInfo);
        }
        else
        {
            espObj = new ESPObject(obj, mobInfo);
            pool.Add(espObj);
        }

        used++;
        return espObj;
    }

    private static void UnbindPool(List<ESPObject> pool)
    {
        foreach (var espObj in pool) espObj.Unbind();
    }

}
