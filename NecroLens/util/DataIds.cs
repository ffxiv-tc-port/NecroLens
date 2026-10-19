using System.Collections.Generic;
using NecroLens.Data;

namespace NecroLens.util;

public static class DataIds
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // LogMessage 資料表列號。
    // 注意:這些是 Excel 列號,不是封包 opcode —— 列號跨改版穩定,而且各語系都取得到在地化文字,
    // 所以能用「比對系統訊息內文」的方式偵測,不必解析封包。
    // 其餘事件(進入/離開、換層、魔陶器使用)由 InstanceContentDeepDungeon 結構輪詢取得,不需要列號。
    public const uint LogHoardDiscovered = 7274;     // 發現了埋藏的寶藏!
    public const uint LogHoardObtained = 7275;       // 獲得了埋藏的寶藏!
    public const uint LogHoardObtainedByOther = 7276; // 等人獲得了埋藏的寶藏!
    public const uint LogItemCappedPotd = 7222;      // 無法獲得更多的◯◯了。被重新放回了寶箱中……
    public const uint LogItemCappedEo = 9208;        // 同上,正統優雷卡用的變體

    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // DataIds of Objects
    public const uint SilverChest = 2007357;
    public const uint GoldChest = 2007358;
    public const uint MimicChest = 2006020;

    public const uint AccursedHoard = 2007542;
    public const uint AccursedHoardCoffer = 2007543;

    public const uint ItemPenaltyStatusId = 1094;

    public static readonly HashSet<uint> PalaceOfTheDeadMapIds = new()
    {
        561, 562, 563, 564, 565, 593, 594, 595, 596, 597, 598, 599, 600, 601, 602, 603, 604, 605, 606, 607
    };

    public static readonly HashSet<uint> HeavenOnHighMapIds = new()
    {
        770, 771, 772, 782, 773, 783, 774, 784, 775, 785
    };

    public static readonly HashSet<uint> EurekaOrthosMapIds = new()
    {
        1099, 1100, 1101, 1102, 1103, 1104, 1105, 1106, 1107, 1108
    };

    public static readonly HashSet<uint> PilgrimsTraverseMapIds = new()
    {
        1281, 1282, 1283, 1284, 1285, 1286, 1287, 1288, 1289, 1290
    };

    public static readonly HashSet<uint> IgnoredDataIDs = new()
    {
        0,       // Players
        6388,    // Triggered Trap
        1023070, // ??? Object way out
        2000608, // ??? Object in Boss Room
        2005809, // Exit
        2001168, // Twistaaa

        // Random friendly stuff
        15898, 15899, 15860,
        18867, 18868, 18869, 
        10489, 16926, 7245,
        13961, 10487
    };

    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // 擬態怪(抖動的寶箱)。🔴 這裡比對的是 IGameObject.DataId(＝BNpcBase id / BaseId),不是 NameId ——
    //    兩者是完全不同的 id 空間,填錯的失敗形式是「靜默永不命中」,不會有任何錯誤訊息。
    // 各深宮的區塊在 BNpcBase 表裡是「N 列擬態怪(ModelChara 648/1526/1527 ＝銅/銀/金寶箱)緊接 N 列友方(ModelChara 1046)」。
    // ⚠️ 未證實假設:「9042-9061 屬於天之御柱」是結構簽名推論,尚未實機驗證;不成立的代價只是顯示錯標,不會崩潰。
    public static readonly HashSet<uint> MimicIDs = new()
    {
        // 上游既有值,來源不明,刻意保留：6362/6363 的 ModelChara 是金寶箱(寶藏地圖迷宮那一段);
        // 2566 的 ModelChara 不是寶箱,疑似 NameId 誤植。
        2566, 6362, 6363,
        // 死者宮殿金寶箱 51-60 層（RedAsteroid fork 標註，未實機驗證）；BNpcBase.ModelChara 1527＝金寶箱外觀
        6359,
        // 死者宮殿(5831-5835 整個區塊)
        5831, 5832, 5833, 5834, 5835,
        // 天之御柱(9042-9051 整個區塊;未證實推論)
        9042, 9043, 9044, 9045, 9046, 9047, 9048, 9049, 9050, 9051,
        // 正統優雷卡(15996-16005 整個區塊)
        15996, 15997, 15998, 15999, 16000, 16001, 16002, 16003, 16004, 16005,
        // 巡禮道
        18889, 18890
    };

    public static readonly HashSet<uint> BronzeChestIDs = new()
    {
        // PotD
        782, 783, 784, 785, 786, 787, 788, 789, 790, 802, 803, 804, 805,
        // HoH
        1036, 1037, 1038, 1039, 1040, 1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049,
        // EO
        1541, 1542, 1543, 1544, 1545, 1546, 1547, 1548, 1549, 1550, 1551, 1552, 1553, 1554,
        // PT
        1882, 1884, 1885, 1886, 1888, 1889, 1890, 1891, 1892, 1893, 1906, 1907, 1908, 
    };

    public static readonly Dictionary<uint, string> TrapIDs = new()
    {
        { 2007182, Strings.Traps_Landmine },
        { 2007183, Strings.Traps_Luring_Trap },
        { 2007184, Strings.Traps_Enfeebling_Trap },
        { 2007185, Strings.Traps_Impeding_Trap },
        { 2007186, Strings.Traps_Toad_Trap },
        { 2009504, Strings.Traps_Odder_Trap },
        { 2013284, Strings.Traps_Owlet_Trap },
        { 2014939, Strings.Traps_Fae_Trap },
    };

    public static readonly HashSet<uint> PassageIDs = new()
    {
        2007188, // PotD
        2009507, // HoH
        2013287, // EO
        2014756  // PT
    };

    public static readonly HashSet<uint> ReturnIDs = new()
    {
        2007187, // PotD
        2009506, // HoH
        2013286,  // EO
        2014755
    };

    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // 友方(不會主動攻擊的同伴型敵人)。與 MimicIDs 一樣比對 DataId(BaseId),不是 NameId。
    // 區塊來源見上方 MimicIDs 的註解(友方 ＝ ModelChara 1046 的連續區塊)。
    public static readonly HashSet<uint> FriendlyIDs = new()
    {
        // 上游既有值,來源不明,刻意保留待查：5041/7610 的 ModelChara 皆為 0,不屬於友方的 1046 家族,疑似 NameId 誤植。
        5041, 7610,
        // 死者宮殿(5836-5840 整個區塊)
        5836, 5837, 5838, 5839, 5840,
        // 天之御柱(9052-9061 整個區塊;同屬上方標註的未證實推論)
        9052, 9053, 9054, 9055, 9056, 9057, 9058, 9059, 9060, 9061,
        // 正統優雷卡(16006-16015 整個區塊)
        16006, 16007, 16008, 16009, 16010, 16011, 16012, 16013, 16014, 16015,
        // 巡禮道
        18898, 18899, 18900
    };

    // Pilgrimage's Traverse Candle Buffs
    public static readonly HashSet<uint> VotifesIds = new()
    {
        2014759
    };
}
