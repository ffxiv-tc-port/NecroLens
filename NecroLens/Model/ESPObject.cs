using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using NecroLens.Data;
using NecroLens.util;

namespace NecroLens.Model;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[Serializable]
public class ESPObject
{
    public enum ESPAggroType
    {
        Sight,
        Sound,
        Proximity
    }

    public enum ESPDangerLevel
    {
        Easy,
        Caution,
        Danger
    }

    public enum ESPType
    {
        Player,
        Enemy,
        Mimic,
        FriendlyEnemy,
        BronzeChest,
        SilverChest,
        GoldChest,
        AccursedHoard,
        AccursedHoardCoffer,
        MimicChest,
        Trap,
        Return,
        Passage,
        Votife,
    }

    private MobInfo? mobInfo;

    public ESPObject(IGameObject gameObject, MobInfo? mobInfo = null)
    {
        ContainingPomander = null;
        GameObject = gameObject;
        this.mobInfo = mobInfo;

        // Mob info exists? check floor overrides
        if (this.mobInfo != null)
        {
            if (DeepDungeonContentInfo.ContentMobInfoChanges.TryGetValue(
                    DungeonService.CurrentContentId, out var overrideInfos))
            {
                var npc = (IBattleNpc)gameObject;
                var mob = overrideInfos.FirstOrDefault(m => m.Id == npc.NameId);
                if (mob != null)
                {
                    this.mobInfo.Patrol = mob.Patrol ?? this.mobInfo.Patrol;
                    this.mobInfo.AggroType = mob.AggroType ?? this.mobInfo.AggroType;
                }
            }

            // 🔴 擬態怪/友方的判定必須在這條分支裡也跑一次,否則天之御柱永遠不會命中。
            // mobInfo 是 ESPService 用 NameId 去查 Data/allMobs.json 得來的,而天之御柱的擬態怪
            // (BNpcName 7392-7394「抖動的寶箱」)正好被收錄在那張表裡 —— 於是 mobInfo != null,
            // 底下那條 else 的整串分類鏈被跳過,Type 永遠停在預設的 Enemy(白色、無擬態怪標記)。
            // 死者宮殿與正統優雷卡沒踩到,只是因為它們的寶箱 NameId(5057 / 13298)剛好不在
            // allMobs.json 裡,所以走的是 else 那條路。
            //
            // 這裡刻意只補 FriendlyIDs / MimicIDs 兩項,而且只認明確列在那兩個集合裡的 BaseId,
            // 其餘物件的既有判定完全不受影響。ESPService.DoDrawName() 對 Enemy / Mimic /
            // FriendlyEnemy 三者的顯示條件相同(都是 !InCombat()),所以這個改動只會換掉顏色與
            // 圖示,不會讓任何原本畫得出來的東西消失。
            var battleNpcDataId = gameObject.BaseId;
            if (DataIds.FriendlyIDs.Contains(battleNpcDataId))
                Type = ESPType.FriendlyEnemy;
            else if (DataIds.MimicIDs.Contains(battleNpcDataId))
                Type = ESPType.Mimic;
        }

        // No MobInfo? Must be an other object
        else
        {
            var dataId = gameObject.BaseId;

            if (ObjectTable.LocalPlayer != null && ObjectTable.LocalPlayer.EntityId == gameObject.EntityId)
                Type = ESPType.Player;
            else if (DataIds.BronzeChestIDs.Contains(dataId))
                Type = ESPType.BronzeChest;
            else if (DataIds.SilverChest == dataId)
                Type = ESPType.SilverChest;
            else if (DataIds.GoldChest == dataId)
                Type = ESPType.GoldChest;
            else if (DataIds.MimicChest == dataId)
                Type = ESPType.MimicChest;
            else if (DataIds.AccursedHoard == dataId)
                Type = ESPType.AccursedHoard;
            else if (DataIds.AccursedHoardCoffer == dataId)
                Type = ESPType.AccursedHoardCoffer;
            else if (DataIds.PassageIDs.Contains(dataId))
                Type = ESPType.Passage;
            else if (DataIds.ReturnIDs.Contains(dataId))
                Type = ESPType.Return;
            else if (DataIds.TrapIDs.ContainsKey(dataId))
                Type = ESPType.Trap;
            else if (DataIds.FriendlyIDs.Contains(dataId))
                Type = ESPType.FriendlyEnemy;
            else if (DataIds.MimicIDs.Contains(dataId))
                Type = ESPType.Mimic;
            else if (DataIds.VotifesIds.Contains(dataId))
                Type = ESPType.Votife;
        }
    }
    
    public Pomander? ContainingPomander { get; set; }

    public IGameObject GameObject { get; }

    public ESPType Type { get; set; } = ESPType.Enemy;

    /**
     * Default view of a Sight mob is 90° in front. We use the radian value of cos 90°.
     */
    public float SightRadian { get; set; } = 1.571f;

    /**
     * Most monsters have different aggro distances. 10.8y is roughly a safe value. Expect PotD Mimics ... 14.6 ._.
     */
    public float AggroDistance()
    {
        return GameObject.HitboxRadius + (Type == ESPType.Mimic && DeepDungeonUtil.InPotD ? 14f : 10f);
    }

    public ESPAggroType AggroType()
    {
        return mobInfo?.AggroType ?? ESPAggroType.Proximity;
    }

    public ESPDangerLevel DangerLevel()
    {
        return mobInfo?.DangerLevel ?? ESPDangerLevel.Easy;
    }

    public bool IsBossOrAdd()
    {
        return mobInfo?.BossOrAdd ?? false;
    }

    public bool IsSpecialMob()
    {
        return mobInfo?.Special ?? false;
    }

    public bool IsPatrol()
    {
        // heavenly onmitsu exists twice, one partol one not. Only DataId differs
        if (mobInfo != null && mobInfo.Id == 7305)
        {
            return GameObject.BaseId == 8922;
        }

        return mobInfo?.Patrol ?? false;
    }

    public float InteractionDistance()
    {
        return Type switch
        {
            ESPType.BronzeChest => 3.1f,
            ESPType.SilverChest => 4.4f,
            ESPType.GoldChest => 4.4f,
            ESPType.AccursedHoardCoffer => 4.4f,
            _ => 2f
        };
    }

    public float Distance()
    {
        return ObjectTable.LocalPlayer != null ? GameObject.Position.Distance2D(ObjectTable.LocalPlayer.Position) : 0;
    }

    public bool IsChest()
    {
        return Type is ESPType.BronzeChest or ESPType.SilverChest or ESPType.GoldChest or ESPType.AccursedHoardCoffer;
    }

    public uint RenderColor()
    {
        switch (Type)
        {
            case ESPType.Enemy:
                return DangerLevel() switch
                {
                    ESPDangerLevel.Danger => Color.Red.ToUint(),
                    ESPDangerLevel.Caution => Color.OrangeRed.ToUint(),
                    _ => Color.White.ToUint()
                };
            case ESPType.FriendlyEnemy:
                return Color.LightGreen.ToUint();
            case ESPType.Mimic:
            case ESPType.MimicChest:
            case ESPType.Trap:
                return Color.Red.ToUint();
            case ESPType.Return:
                return Color.LightBlue.ToUint();
            case ESPType.Passage:
                return Config.PassageColor;
            case ESPType.AccursedHoard:
            case ESPType.AccursedHoardCoffer:
                return Config.HoardColor;
            case ESPType.GoldChest:
                return Config.GoldCofferColor;
            case ESPType.SilverChest:
                return Config.SilverCofferColor;
            case ESPType.BronzeChest:
                return Config.BronzeCofferColor;
            case ESPType.Votife:
                return Config.VotifeColor;
            default:
                return Color.White.ToUint();
        }
    }

    public bool InCombat()
    {
        unsafe
        {
            try
            {
                if (!GameObject.IsValid() || GameObject is not IBattleNpc) return true;
                // Using dalamud's status flags here sometimes causes game crashes 
                return ((BattleChara*)GameObject.Address)->Character.InCombat;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public string? NameSymbol()
    {
        if (IsSpecialMob()) return "\uE0C0";
        if (IsPatrol()) return "\uE05E";

        return Type switch
        {
            ESPType.Trap => "\uE0BF",
            ESPType.AccursedHoard => "\uE03C",
            ESPType.BronzeChest => "\uE03D",
            ESPType.SilverChest => "\uE03D",
            ESPType.GoldChest => "\uE03D",
            ESPType.Return => "\uE03B",
            ESPType.Passage => "\uE035",
            ESPType.FriendlyEnemy => "\uE034",
            ESPType.Votife => "\uE03B",
            _ => null
        };
    }

    public string Name()
    {
        // We dont wanna see Bosses and Adds
        if (IsBossOrAdd()) return "";

        // No name for all "Enemies" (default type) which are not hostile
        if (Type == ESPType.Enemy && !BattleNpcSubKind.Enemy.Equals((BattleNpcSubKind)GameObject.SubKind))
            return "";

        var name = "";
        var symbol = NameSymbol();
        if (symbol != null)
            name += symbol + " ";

        name += Type switch
        {
            ESPType.Trap => DataIds.TrapIDs.TryGetValue(GameObject.BaseId, out var value)
                                ? value
                                : Strings.Traps_Unknown,
            ESPType.AccursedHoard => Strings.Chest_Accursed_Hoard,
            ESPType.BronzeChest => Strings.Chest_Bronze_Chest,
            ESPType.SilverChest => Strings.Chest_Silver_Chest,
            ESPType.GoldChest => Strings.Chest_Gold_Chest,
            ESPType.MimicChest => Strings.Chest_Mimic,
            _ => GameObject.Name.TextValue
        };

        name += Type switch
        {
            ESPType.Passage => " - " + Distance().ToString("0.0"),
            _ => ""
        };


        if (Config.ShowDebugInformation)
        {
            name += "\nD:" + GameObject.BaseId;
            if (GameObject is IBattleNpc npc2) name += " N:" + npc2.NameId;
        }

        return name;
    }
}
