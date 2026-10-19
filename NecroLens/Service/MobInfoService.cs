using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using NecroLens.Model;
using Newtonsoft.Json;

namespace NecroLens.Service;

public class MobInfoService : IDisposable
{
    public readonly Dictionary<uint, MobInfo> MobInfoDictionary;

    public MobInfoService()
    {
        MobInfoDictionary = new Dictionary<uint, MobInfo>();
        LoadDeepDungeonMobInfos();
    }

    public void Dispose()
    {
        MobInfoDictionary.Clear();
    }

    private bool _loadingFallback;

    // 只在第一次成功抓到備援檔時印一行來源；之後的重試沿用既有的 (from backup) 那行。
    // 讀寫都在框架執行緒上（RunOnFrameworkThread 的接續裡），不需要額外同步。
    private bool _loggedBackupSource;

    private void LoadDeepDungeonMobInfos()
    {
        PluginLog.Info("Loading Mob infos...");
        try
        {
            LoadMobInfoFile(Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "data/allMobs.json"));
        }
        catch (Exception e)
        {
            PluginLog.Error("Unable to load MobInfo!", e);
        }

        if (MobInfoDictionary.Count <= 0)
            LoadFallbackAsync();

        PluginLog.Information($"Loaded infos for {MobInfoDictionary.Count} mobs!");
    }

    // Only reached if the bundled data/allMobs.json failed to load. TryReloadIfEmpty() (and
    // therefore this) can run at runtime - e.g. from DeepDungeonService on entering a deep
    // dungeon - not just at plugin startup, so the network fallback must not block the caller.
    private void LoadFallbackAsync()
    {
        if (_loadingFallback)
            return;
        _loadingFallback = true;

        PluginLog.Info("Mob infos empty. Retry backup method.");
        // 指向本 org 的 fork，不是上游 Jukkales/NecroLens：這份備援資料要跟隨我們自己的
        // 修補（例如死者宮殿／正統優雷卡補齊的擬態怪與友方 id），抓上游會拿回沒有那些
        // 修補的版本，而且是靜默的——備援路徑本來就只在主檔載入失敗時才走。
        // 🔴 ref 用 HEAD 而不是寫死分支名：寫死的分支名在改名或換預設分支的那天會 404，而這裡的 404 只會變成一行 log，
        //    使用者看到的是「疊加層什麼都不顯示」。HEAD 由 GitHub 解析成該 repo 當下的預設分支。
        const string uri = "https://raw.githubusercontent.com/ffxiv-tc-port/NecroLens/HEAD/NecroLens/Data/allMobs.json";
        var backupUri = new Uri(uri);

        Task.Run(async () =>
        {
            List<MobInfo>? result = null;
            Exception? error = null;
            try
            {
                result = await Load<List<MobInfo>>(backupUri).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                error = e;
            }

            _ = Framework.RunOnFrameworkThread(() =>
            {
                if (error != null)
                {
                    PluginLog.Error("Unable to load MobInfo from backup location! Panic!", error);
                }
                else if (result != null)
                {
                    foreach (var mobInfo in result)
                        MobInfoDictionary[mobInfo.Id] = mobInfo;
                    if (!_loggedBackupSource)
                    {
                        _loggedBackupSource = true;
                        PluginLog.Information(
                            $"MobInfo backup fetched from {backupUri.Host} (Dalamud GitHub client).");
                    }

                    PluginLog.Information($"Loaded infos for {MobInfoDictionary.Count} mobs (from backup)!");
                }
                else
                {
                    PluginLog.Error("Unable to load MobInfo from backup location! Panic!");
                }

                _loadingFallback = false;
            });
        });
    }

    // 本 org 轉 private 之後，raw.githubusercontent.com 上的備援檔要帶使用者在 Dalamud 設定的
    // GitHub 權杖才抓得到。CreateGitHubHttpClient() 只對本 org 的 raw/api 主機加那個標頭，
    // 其他主機逐位元組不變；沒設權杖時它跟一般 client 一樣，所以既有的失敗退回路徑不變。
    // 它回的是新的 client，所有權在呼叫端 ⇒ 這裡要 using 掉。
    public static async Task<T?> Load<T>(Uri uri)
    {
        using var client = PluginInterface.CreateGitHubHttpClient();
        var result = await client.GetAsync(uri).ConfigureAwait(true);
        return result.IsSuccessStatusCode ? await result.Content.ReadFromJsonAsync<T>().ConfigureAwait(true) : default;
    }

    private void LoadMobInfoFile(string path)
    {
        var info = JsonConvert.DeserializeObject<List<MobInfo>>(File.ReadAllText(path));
        if (info != null)
        {
            foreach (var mobInfo in info)
                MobInfoDictionary[mobInfo.Id] = mobInfo;
        }
        else
        {
            MobInfoDictionary.Clear();
            PluginLog.Error($"Unable to load MobInfo file {path}!");
        }
    }

    public void Reload()
    {
        MobInfoDictionary.Clear();
        LoadDeepDungeonMobInfos();
    }

    public void TryReloadIfEmpty()
    {
        if (MobInfoDictionary.Count <= 0)
            Reload();
    }
}
