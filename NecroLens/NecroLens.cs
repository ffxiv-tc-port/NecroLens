#undef DEBUG


using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using ECommons;
using NecroLens.Data;
using NecroLens.Model;
using NecroLens.Service;
using NecroLens.Windows;

namespace NecroLens;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedType.Global")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public sealed class NecroLens : IDalamudPlugin
{
    private readonly ConfigWindow configWindow;
    private readonly DeepDungeonService deepDungeonService;
    private readonly ESPService espService;
    private readonly MainWindow mainWindow;
    private readonly MobInfoService mobInfoService;
    private readonly MobVulnerabilityService mobVulnerabilityService;
    private readonly PluginCommands pluginCommands;

    public readonly WindowSystem WindowSystem = new("NecroLens");

#if DEBUG
    private readonly ESPTestService espTestService;
#endif

    public NecroLens(IDalamudPluginInterface? pluginInterface)
    {
        pluginInterface?.Create<PluginService>();
        Plugin = this;

        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);

        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // 指令的 HelpMessage 在 PluginCommands 建構當下就從 resx 取值並固定下來,
        // 語系必須在那之前套用,否則 /xlhelp 與插件安裝器的指令清單會落回英文。
        ApplyLanguage();

        pluginCommands = new PluginCommands();
        configWindow = new ConfigWindow();
        mainWindow = new MainWindow();

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        mobInfoService = new MobInfoService();
        MobService = mobInfoService;

        mobVulnerabilityService = new MobVulnerabilityService();
        VulnerabilityService = mobVulnerabilityService;

        espService = new ESPService();
        EspService = espService;

        deepDungeonService = new DeepDungeonService();
        DungeonService = deepDungeonService;
#if DEBUG
        espTestService = new ESPTestService();
#endif
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ShowConfigWindow;
    }

    // 只設本插件的資源文化，不動行程全域的 DefaultThreadCurrentUICulture（那會改到其他插件，且平行載入下時機不固定）。
    // 台服客戶端回報 ClientLanguage 7，舊版 Dalamud 回報 4；用數值比較，列舉裡有沒有 7 這個名字都編得過。
    internal static void ApplyLanguage()
    {
        Strings.Culture = Config.Language != ""
            ? CultureInfo.GetCultureInfo(Config.Language)
            : ClientState.ClientLanguage switch
            {
                ClientLanguage.French => CultureInfo.GetCultureInfo("fr"),
                ClientLanguage.German => CultureInfo.GetCultureInfo("de"),
                ClientLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
                (ClientLanguage)4 or (ClientLanguage)5 or (ClientLanguage)7 => CultureInfo.GetCultureInfo("zh-Hant"),
                _ => CultureInfo.GetCultureInfo("en")
            };
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ShowConfigWindow;

        configWindow.Dispose();
        pluginCommands.Dispose();
        mainWindow.Dispose();
        espService.Dispose();
        deepDungeonService.Dispose();
#if DEBUG
        espTestService.Dispose();
#endif
        mobInfoService.Dispose();
        mobVulnerabilityService.Dispose();
        
        ECommonsMain.Dispose();
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void ShowMainWindow()
    {
        mainWindow.IsOpen = true;
    }

    public void CloseMainWindow()
    {
        mainWindow.IsOpen = false;
    }

    public void ShowConfigWindow()
    {
        configWindow.IsOpen = true;
    }
}
