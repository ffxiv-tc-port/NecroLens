using System;
using System.Diagnostics.CodeAnalysis;
using Dalamud.Configuration;

namespace NecroLens.Model;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[Serializable]
public class Configuration : IPluginConfiguration
{
    public bool AutoOpenOnEnter { get; set; } = true;

    public bool EnableESP { get; set; } = true;

    public bool ShowPlayerDot { get; set; } = true;
    public bool ShowMobViews { get; set; } = true;
    public bool ShowCofferInteractionRange { get; set; } = true;
    public bool ShowPatrolArrow { get; set; } = true;

    // 需要 MonsterDex 才會顯示任何東西；沒裝時整個功能靜默關閉，所以預設開著不會有副作用。
    public bool ShowMobVulnerabilities { get; set; } = true;

    // 名稱標籤字級＝Dalamud 預設字型大小的倍數。既有使用者的設定檔沒有這個鍵，吃到的就是這個初始值。
    public float NameLabelScale { get; set; } = 1.3f;

    // 只影響顯示；自動開箱的安全判斷不讀它。
    public bool ShowPossibleMimicChests { get; set; } = true;

    public bool HighlightCoffers { get; set; } = true;
    public bool HighlightPassage { get; set; } = true;

    public bool ShowBronzeCoffers { get; set; } = true;
    public bool ShowSilverCoffers { get; set; } = true;
    public bool ShowGoldCoffers { get; set; } = true;
    public bool ShowHoards { get; set; } = true;

    public bool ShowPassage { get; set; } = true;
    public bool ShowReturn { get; set; } = true;
    public bool ShowTraps { get; set; } = true;
    public bool ShowMimicCoffer { get; set; } = true;
    public bool ShowVotife { get; set; } = true;

    public bool OpenChests { get; set; } = false;
    public bool OpenBronzeCoffers { get; set; } = true;
    public bool OpenSilverCoffers { get; set; } = true;
    public bool OpenGoldCoffers { get; set; } = true;
    public bool OpenHoards { get; set; } = true;

    public bool OpenUnsafeChests { get; set; } = false;

    public int Version { get; set; } = 1;
    
    public uint PlayerDotColor { get; set; } = 0xCC0000FF;
    public uint NormalAggroColor { get; set; } = 0xFF2A2AA5;
    public uint SoundAggroColor { get; set; } = 0xFFFF00FF;
    public uint PassageColor { get; set; } = 0xFFD0E040;
    public uint VotifeColor { get; set; } = 0xFFD0E040;
    
    public uint BronzeCofferColor { get; set; } = 0xFF13458B;
    public uint SilverCofferColor { get; set; } = 0xFFC0C0C0;
    public uint GoldCofferColor { get; set; } = 0xFF00D7FF;
    public uint HoardColor { get; set; } = 0xFF00D7FF;
    
    public bool ShowDebugInformation { get; set; } = false;
    
    // 刻意不再讀：上傳到作者伺服器的資料收集已移除（深層迷宮資料只下載不上傳）。
    // 欄位保留，舊設定檔照常反序列化、存檔時原樣寫回。
    public string? UniqueId { get; set; }
    public bool OptInDataCollection { get; set; } = false;
    public string Language { get; set; } = "";

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
    }
}
