using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NecroLens.util;
using Newtonsoft.Json;
using static NecroLens.util.DeepDungeonUtil;

namespace NecroLens.Model;

public class FloorDetails
{
    public readonly Dictionary<uint, Pomander> DoubleChests = new();
    private readonly List<Pomander> floorEffects = [];
    public readonly Dictionary<uint, FloorObject> FloorObjects = new();
    public readonly List<uint> InteractionList = [];

    private readonly List<Pomander> usedPomanders = [];
    public int CurrentFloor;
    public DateTime FloorStartTime;
    public bool FloorTransfer;
    public bool FloorVerified;
    public bool HoardFound;
    public DateTime NextRespawn;

    public int RespawnTime;

    public void Clear()
    {
        usedPomanders.Clear();
        floorEffects.Clear();
        InteractionList.Clear();
        FloorObjects.Clear();
        DoubleChests.Clear();
        FloorVerified = false;
        CurrentFloor = 0;
        FloorTransfer = false;
    }

    public void NextFloor()
    {
        if (FloorTransfer)
        {
            PluginLog.Debug($"NextFloor: {CurrentFloor + 1}");

            // Reset
            InteractionList.Clear();
            FloorObjects.Clear();
            DoubleChests.Clear();

            // Apply effects
            floorEffects.Clear();
            if (usedPomanders.ContainsAny(Pomander.Affluence, Pomander.AffluenceProtomander))
                floorEffects.Add(Pomander.Affluence);

            if (usedPomanders.ContainsAny(Pomander.Alteration, Pomander.AlterationProtomander))
                floorEffects.Add(Pomander.Alteration);

            if (usedPomanders.ContainsAny(Pomander.Flight, Pomander.FlightProtomander))
                floorEffects.Add(Pomander.Flight);

            usedPomanders.Clear();
            HoardFound = false;
            CurrentFloor++;
            FloorStartTime = DateTime.Now;
            NextRespawn = DateTime.Now.AddSeconds(RespawnTime);
            FloorTransfer = false;
        }
    }

    /**
     * 樓層數不再靠解析 DeepDungeonMap 視窗的文字節點(那條路徑會因為版本/語系不同而
     * 走空指標或 int.Parse 失敗)。現在由 DeepDungeonService 直接寫入
     * InstanceContentDeepDungeon.Floor 的值。
     */
    public unsafe int PassageProgress()
    {
        // 這條路徑每幀都會被 MainWindow 呼叫。原生指標只在這次呼叫內使用、不跨幀保存,
        // 每一跳都做 null 與型別檢查,任何一跳失敗就回傳 0(畫面顯示為「未開啟」)。
        // 刻意不使用 try/catch:懸空指標造成的 AccessViolationException 在 .NET Core 屬於
        // corrupted-state exception,try/catch 完全攔不到,加了只會製造假的安全感。
        if (!TryGetAddonByName<AtkUnitBase>("DeepDungeonMap", out var addon) || !IsAddonReady(addon))
            return 0;

        // 不倚賴 IsAddonReady 的內部實作,自己再確認一次 uld 已載入完成;
        // 未載入完成時 NodeList / NodeListCount 可能未初始化或已失效。
        if (addon->UldManager.LoadedState != AtkLoadState.Loaded)
            return 0;

        var container = addon->GetNodeById(16);
        if (container == null)
            return 0;

        var child = container->ChildNode;
        if (child == null)
            return 0;

        var key = child->PrevSiblingNode;
        // AtkResNode 的結構大小是 0xB0,而 AtkComponentNode.Component 位在 0xB0,
        // 少了 Type 檢查(component 節點一律 >= 1000)就會讀到配置範圍外的記憶體。
        if (key == null || (int)key->Type < 1000)
            return 0;

        var component = ((AtkComponentNode*)key)->Component;
        if (component == null || component->UldManager.LoadedState != AtkLoadState.Loaded)
            return 0;

        // 存取 NodeList[1] 之前必須先驗上界,原本的寫法缺這個檢查。
        ref var uld = ref component->UldManager;
        if (uld.NodeList == null || uld.NodeListCount <= 1)
            return 0;

        var imageNode = uld.NodeList[1];
        if (imageNode == null || imageNode->Type != NodeType.Image)
            return 0;

        return ((AtkImageNode*)imageNode)->PartId * 10;
    }

    public void OnPomanderUsed(Pomander pomander)
    {
        PluginLog.Debug($"Pomander ID: {pomander}");

        if (InEO)
        {
            if (pomander is >= Pomander.Safety and <= Pomander.Serenity) pomander -= 22;

            if (pomander is Pomander.Intuition or Pomander.Raising) pomander -= 20;
        }

        if (pomander is Pomander.Affluence or Pomander.Flight or Pomander.Alteration)
            usedPomanders.Add(pomander);
        else
        {
            floorEffects.Add(pomander);
            usedPomanders.Add(pomander);
        }
    }

    public DeepDungeonTrapStatus TrapStatus()
    {
        if (floorEffects.ContainsAny(Pomander.Safety, Pomander.SafetyProtomander))
            return DeepDungeonTrapStatus.Inactive;

        if (floorEffects.ContainsAny(Pomander.Sight, Pomander.SightProtomander)) return DeepDungeonTrapStatus.Visible;

        return DeepDungeonTrapStatus.Active;
    }

    public bool HasRespawn()
    {
        return !(CurrentFloor % 10 == 0 || (InEO && CurrentFloor == 99));
    }

    public int TimeTillRespawn()
    {
        return (int)(DateTime.Now - NextRespawn).TotalSeconds;
    }

    public int UpdateFloorTime()
    {
        var now = DateTime.Now;
        var time = (int)(now - FloorStartTime).TotalSeconds;
        if (now > NextRespawn) NextRespawn = now.AddSeconds(RespawnTime);
        return time;
    }

    public void TrackFloorObjects(ESPObject espObj, int currentContentId)
    {
        if (FloorTransfer
            || IsIgnored(espObj.GameObject.BaseId)
            || FloorObjects.ContainsKey(espObj.GameObject.EntityId)) return;

        var obj = new FloorObject();
        obj.DataId = espObj.GameObject.BaseId;
        if (espObj.GameObject is IBattleNpc npcObj)
        {
            obj.NameId = npcObj.NameId;
            obj.Name = npcObj.Name.TextValue;
        }

        obj.ContentId = currentContentId;
        obj.Floor = CurrentFloor;
        obj.HitboxRadius = espObj.GameObject.HitboxRadius;
        FloorObjects[espObj.GameObject.EntityId] = obj;
    }

    private bool IsIgnored(uint dataId)
    {
        return DataIds.ReturnIDs.Contains(dataId)
               || DataIds.PassageIDs.Contains(dataId)
               || DataIds.TrapIDs.ContainsKey(dataId)
               || DataIds.GoldChest == dataId
               || DataIds.SilverChest == dataId
               || DataIds.MimicChest == dataId
               || DataIds.BronzeChestIDs.Contains(dataId)
               || DataIds.AccursedHoard == dataId
               || DataIds.AccursedHoardCoffer == dataId;
    }

    public void DumpFloorObjects(int currentContentId)
    {
        if (Config.OptInDataCollection)
        {
            var result = new Dictionary<uint, DataCollector.MobData>();

            foreach (var keyValuePair in FloorObjects)
            {
                DataCollector.MobData data = new()
                {
                    DataId = keyValuePair.Value.DataId,
                    NameId = keyValuePair.Value.NameId,
                    ContentId = currentContentId,
                    Floor = CurrentFloor,
                    HitboxRadius = keyValuePair.Value.HitboxRadius,
                    MoveTimes = [],     // TODO
                    AggroDistances = [] // TODO
                };
                result.TryAdd(data.DataId, data);
            }

            var collector = new DataCollector
            {
                Sender = Config.UniqueId!,
                Party = PartyList.PartyId.ToString(),
                Data = new Collection<DataCollector.MobData>(result.Values.ToList())
            };

            var json = JsonConvert.SerializeObject(collector,
                                                   Formatting.Indented,
                                                   new JsonSerializerSettings
                                                   {
                                                       NullValueHandling = NullValueHandling.Ignore
                                                   });
            PluginLog.Debug("Sending Data: \n" + json);

            Task.Factory.StartNew(async () =>
            {
                using var client = new HttpClient();
                try
                {
                    await client.PostAsync("https://necrolens.jusrv.de/api/import2",
                                           new StringContent(json, Encoding.UTF8, "application/json"));
                }
                catch (Exception e)
                {
                    PluginLog.Debug(e, "Failed to send data to server");
                }
            });
        }
    }

    public List<Pomander> GetFloorEffects()
    {
        return floorEffects.OrderBy(e => e.ToString()).ToList();
    }

    public bool IsNextFloorWith(Pomander pomander)
    {
        return usedPomanders.Contains(pomander);
    }
}
