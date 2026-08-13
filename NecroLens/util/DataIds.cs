using System.Collections.Generic;
using NecroLens.Data;

namespace NecroLens.util;

public static class DataIds
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // LogMessage 資料表列號。
    // 注意:這些是 Excel 列號,不是封包 opcode —— 列號跨改版穩定,而且各語系都取得到在地化文字,
    // 所以能用「比對系統訊息內文」的方式偵測,不必解析封包。已對台服 7.20 的 LogMessage 表查證。
    // 其餘原本靠封包分派的事件(進入/離開、換層、魔陶器使用)已改由
    // InstanceContentDeepDungeon 結構輪詢取得,不再需要對應的列號。
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
    // 擬態怪(抖動的寶箱)。
    // 🔴 這裡比對的是 IGameObject.DataId(＝BNpcBase id / BaseId),不是 NameId ——
    //    兩者是完全不同的 id 空間,填錯的失敗形式是「靜默永不命中」,不會有任何錯誤訊息。
    //
    // 驗證法(2026-08-13 用台服 7.20 EXD dump `exd-tc/7.20/BNpcBase.csv` 全表掃描實測):
    //   深宮的擬態怪與友方在 BNpcBase 表裡是「N 列擬態怪緊接 N 列友方」的連續區塊,
    //   擬態怪的 ModelChara ∈ {648, 1526, 1527}(三者都是 Model 197 的 Variant 1/2/3 ＝銅/銀/金寶箱),
    //   友方的 ModelChara 固定 1046。整張表只有三組符合這個簽名:
    //     死者宮殿    5831-5835  (648×3, 1526×1, 1527×1) / 友方 5836-5840  (1046×5)
    //     天之御柱    9042-9051  (648×3, 1526×3, 1527×4) / 友方 9052-9061  (1046×10)
    //     正統優雷卡  15996-16005(648×3, 1526×3, 1527×4) / 友方 16006-16015(1046×10)
    //   天之御柱與正統優雷卡的區塊形狀逐格相同(3/3/4 ＋ 10 個 1046)。
    //
    // ⚠️ 未證實假設:「9042-9061 屬於天之御柱」是結構簽名推論,尚未實機驗證。
    //    佐證:BNpcName 7386-7394 全是「天之～」開頭(天之御柱的怪),其中 7392/7393/7394
    //    三列都叫「抖動的寶箱」,正好對應銅/銀/金三階;而 BNpcBase 空間比 BNpcName 空間超前
    //    約 1.2 倍(死者宮殿 5831 vs 5057、天之御柱 9042 vs 7392、正統優雷卡 15996 vs 13298
    //    比值一致)。
    //    ⇒ 推論若不成立,代價是「把某些怪標成擬態怪」的顯示錯標,不會崩潰、不影響其他判定。
    //
    // 📌 2026-08-13 移除原本掛在天之御柱名下的 7392, 7393, 7394:
    //    那三個是 BNpcName(NameId)的「抖動的寶箱」,被誤當成 BaseId 抄進來。
    //    以 BaseId 讀,BNpcBase 7392/7393/7394 的 ModelChara 是 480/2449/2060,不是寶箱模型;
    //    而且三者都出現在 Data/allMobs.json(該表以 NameId 為鍵),可證來源就是 NameId 表。
    //    留著不會命中真正的擬態怪,只會在剛好有那些 BaseId 的物件出現時畫出錯誤標記,故移除。
    public static readonly HashSet<uint> MimicIDs = new()
    {
        // 上游既有值,來源不明,本次未動:
        //   6362/6363 的 BNpcBase ModelChara 確實是 1527(金寶箱),屬於寶藏地圖迷宮那一段區塊;
        //   2566 的 ModelChara 是 65(非寶箱),而 BNpcName 2566 剛好叫「擬態怪」—— 疑似同類 NameId
        //   誤植,但不在本次裁決範圍內,保留。
        2566, 6362, 6363,
        // 死者宮殿(上游只收錄了 5831-5835 區塊裡的三個,本次未擴充)
        5832, 5834, 5835,
        // 天之御柱(2026-08-13 依上述簽名整個區塊收錄)
        9042, 9043, 9044, 9045, 9046, 9047, 9048, 9049, 9050, 9051,
        // 正統優雷卡(上游只收錄了 15996-16005 區塊裡的五個,本次未擴充)
        15997, 15998, 15999, 16002, 16003,
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
    // 區塊來源與驗證法見上方 MimicIDs 的註解(友方 ＝ ModelChara 1046 的連續區塊)。
    //
    // 📌 2026-08-13 移除原本掛在天之御柱名下的 7396, 7397, 7398:
    //    以 NameId 讀,那三個是「珀犬 / 犬神 / 仙狸」—— 天之御柱的雜魚,不是友方;
    //    以 BaseId 讀,BNpcBase 7396/7397/7398 的 ModelChara 是 1799/1619/2015,不屬於友方的
    //    1046 家族。兩種讀法都不成立。
    //    而且那三個 NameId 本來就在 Data/allMobs.json 裡標了 Special: true,
    //    已經由 ESPObject.IsSpecialMob() 另外標示過,這裡是重複又錯位的抄錄,故移除。
    public static readonly HashSet<uint> FriendlyIDs = new()
    {
        // 死者宮殿(5836-5840 才是完整的 1046 區塊,上游只收錄了 5840;
        //           5041/7610 來源不明、BNpcBase 的 ModelChara 皆為 0,不在本次範圍,保留)
        5840, 5041, 7610,
        // 天之御柱(2026-08-13 依 ModelChara 1046 簽名整個區塊收錄;同屬上方標註的未證實推論)
        9052, 9053, 9054, 9055, 9056, 9057, 9058, 9059, 9060, 9061,
        // 正統優雷卡(上游只收錄了 16006-16015 區塊裡的四個,本次未擴充)
        16007, 16008, 16009, 16012,
        // 巡禮道
        18898, 18899, 18900
    };

    // Pilgrimage's Traverse Candle Buffs
    public static readonly HashSet<uint> VotifesIds = new()
    {
        2014759
    };
}
