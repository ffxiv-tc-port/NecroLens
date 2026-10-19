using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Numerics;
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

    private const uint InvalidEntityId = 0xE0000000;
    private const int MaxCachedNames = 512;

    // EntityId -> 該物件的名字。只在繪製執行緒讀寫；BaseId／NameId 對不上就重讀，空名字不快取。
    private static readonly Dictionary<uint, CachedName> NameCache = new();

    private readonly record struct CachedName(uint BaseId, uint NameId, string Name);

    private MobInfo? mobInfo;

    // 綁定當下抄下的身分。address 只拿來和物件表那一格現在的位址比相等，比對成功之前不解參。
    private nint address;
    private ushort objectIndex;
    private ObjectKind objectKind;
    private bool isBattleNpc;
    private bool inCombat = true;

    public ESPObject(IGameObject gameObject, MobInfo? mobInfo = null)
    {
        Bind(gameObject, mobInfo);
    }

    /// <summary>
    /// 綁定到當幀的物件並重算全部狀態（重用物件時用）。<paramref name="gameObject"/> 只在當幀有效：
    /// 這裡把之後要用的值全部抄成純量，不保存它本身（物件表的包裝會被就地改指別的物件）。
    /// </summary>
    internal void Bind(IGameObject gameObject, MobInfo? mobInfo)
    {
        ContainingPomander = null;
        Type = ESPType.Enemy;
        SightRadian = 1.571f;
        this.mobInfo = mobInfo;

        address = gameObject.Address;
        objectIndex = gameObject.ObjectIndex;
        objectKind = gameObject.ObjectKind;
        EntityId = gameObject.EntityId;
        BaseId = gameObject.BaseId;
        SubKind = gameObject.SubKind;
        HitboxRadius = gameObject.HitboxRadius;
        Position = gameObject.Position;
        Rotation = gameObject.Rotation;
        inCombat = true;
        isBattleNpc = gameObject is IBattleNpc;

        // BNpcName row id 在建構當下（framework 執行緒）抄成值。繪製端要拿它去查弱點快取，
        // 而 IBattleNpc.NameId 是解原生指標讀出來的——存下來就不必在繪製執行緒再解一次。
        NameId = gameObject is IBattleNpc battleNpc ? battleNpc.NameId : 0;

        // Mob info exists? check floor overrides
        if (this.mobInfo != null)
        {
            if (DeepDungeonContentInfo.ContentMobInfoChanges.TryGetValue(
                    DungeonService.CurrentContentId, out var overrideInfos))
            {
                var npc = (IBattleNpc)gameObject;
                var npcNameId = npc.NameId;
                MobInfo? mob = null;
                foreach (var m in overrideInfos)
                {
                    if (m.Id == npcNameId)
                    {
                        mob = m;
                        break;
                    }
                }

                if (mob != null)
                {
                    this.mobInfo.Patrol = mob.Patrol ?? this.mobInfo.Patrol;
                    this.mobInfo.AggroType = mob.AggroType ?? this.mobInfo.AggroType;
                }
            }

            // 🔴 擬態怪/友方的判定必須在這條分支裡也跑一次:mobInfo 是用 NameId 查 Data/allMobs.json 得來的,
            // 而天之御柱的擬態怪(BNpcName 7392-7394「抖動的寶箱」)正好被收錄在那張表裡 ⇒ mobInfo != null,
            // 底下那條 else 的整串分類鏈被跳過,Type 會停在預設的 Enemy(白色、無擬態怪標記)。
            // 刻意只補 FriendlyIDs / MimicIDs 兩項,而且只認明確列在那兩個集合裡的 BaseId,其餘物件的既有判定不受影響。
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

    /// <summary>放掉綁定時帶進來的引用；之後到下一次 <see cref="Bind"/> 前不可再使用。</summary>
    internal void Unbind()
    {
        address = 0;
        mobInfo = null;
        ContainingPomander = null;
    }

    /// <summary>
    /// 繪製端每幀先呼叫一次：物件表同一格仍是綁定時那個物件才回 true，並重讀位置、面向、戰鬥狀態；
    /// 回 false＝物件已消失或那一格換了別的物件，這一幀不要畫它。只在框架執行緒呼叫。
    /// </summary>
    internal bool RefreshForDraw()
    {
        var live = Live();
        if (live == null) return false;

        Position = live.Position;
        Rotation = live.Rotation;
        inCombat = ReadInCombat(live);
        return true;
    }

    // 回傳值只能在呼叫端當場用完，不可存起來。
    private IGameObject? Live()
    {
        if (address == 0) return null;
        var live = ObjectTable[objectIndex];
        if (live == null || live.Address != address) return null;
        return live.EntityId == EntityId && live.BaseId == BaseId && live.ObjectKind == objectKind ? live : null;
    }

    private static unsafe bool ReadInCombat(IGameObject gameObject)
    {
        try
        {
            if (!gameObject.IsValid() || gameObject is not IBattleNpc) return true;
            // Using dalamud's status flags here sometimes causes game crashes
            return ((BattleChara*)gameObject.Address)->Character.InCombat;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public Pomander? ContainingPomander { get; set; }

    public uint EntityId { get; private set; }

    public uint BaseId { get; private set; }

    public byte SubKind { get; private set; }

    public float HitboxRadius { get; private set; }

    /// <summary>綁定當下的位置；繪製端經 <see cref="RefreshForDraw"/> 更新成那一幀的位置。</summary>
    public Vector3 Position { get; private set; }

    public float Rotation { get; private set; }

    /// <summary>BNpcName row id；非戰鬥 NPC 為 0。MonsterDex 的弱點 IPC 就是吃這個值。</summary>
    public uint NameId { get; private set; }

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
        return HitboxRadius + (Type == ESPType.Mimic && DeepDungeonUtil.InPotD ? 14f : 10f);
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
            return BaseId == 8922;
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
        return ObjectTable.LocalPlayer != null ? Position.Distance2D(ObjectTable.LocalPlayer.Position) : 0;
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

    /// <summary>最後一次 <see cref="RefreshForDraw"/> 讀到的戰鬥狀態；還沒讀過或讀不到時為 true。</summary>
    public bool InCombat()
    {
        return inCombat;
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
        var label = new LabelBuffer();
        AppendName(label);
        return label.ToString();
    }

    /// <summary>把 <see cref="Name"/> 的文字接到 <paramref name="label"/> 尾端（不配置新字串）。</summary>
    public void AppendName(LabelBuffer label)
    {
        // We dont wanna see Bosses and Adds
        if (IsBossOrAdd()) return;

        // No name for all "Enemies" (default type) which are not hostile
        if (Type == ESPType.Enemy && (BattleNpcSubKind)SubKind != BattleNpcSubKind.Enemy)
            return;

        var symbol = NameSymbol();
        if (symbol != null)
        {
            label.Append(symbol);
            label.Append(' ');
        }

        label.Append(Type switch
        {
            ESPType.Trap => DataIds.TrapIDs.TryGetValue(BaseId, out var value)
                                ? value
                                : Strings.Traps_Unknown,
            ESPType.AccursedHoard => Strings.Chest_Accursed_Hoard,
            ESPType.BronzeChest => Strings.Chest_Bronze_Chest,
            ESPType.SilverChest => Strings.Chest_Silver_Chest,
            ESPType.GoldChest => Strings.Chest_Gold_Chest,
            ESPType.MimicChest => Strings.Chest_Mimic,
            _ => GameObjectName()
        });

        if (Type == ESPType.Passage)
        {
            label.Append(" - ");
            label.Append(Distance(), "0.0");
        }

        if (Config.ShowDebugInformation)
        {
            label.Append("\nD:");
            label.Append(BaseId);
            if (isBattleNpc)
            {
                label.Append(" N:");
                label.Append(NameId);
            }
        }
    }

    // 等同 GameObject.Name.TextValue；那一步每次都要重新解析 SeString，所以同一個物件只解析一次。物件已不在就回空字串。
    private string GameObjectName()
    {
        var entityId = EntityId;
        var baseId = BaseId;
        if (entityId != InvalidEntityId && NameCache.TryGetValue(entityId, out var cached) &&
            cached.BaseId == baseId && cached.NameId == NameId)
            return cached.Name;

        var name = Live()?.Name.TextValue ?? string.Empty;
        if (entityId != InvalidEntityId && name.Length > 0)
        {
            if (NameCache.Count >= MaxCachedNames) NameCache.Clear();
            NameCache[entityId] = new CachedName(baseId, NameId, name);
        }

        return name;
    }
}
