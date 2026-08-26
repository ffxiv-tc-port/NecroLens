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
        //
        // 🔴 ref 用 HEAD 而不是寫死分支名。Splatoon 腳本鏈上同樣的教訓：寫死的分支名在
        //    改名或換預設分支的那天會 404，而這裡的 404 只會變成一行 log，使用者看到的是
        //    「疊加層什麼都不顯示」。HEAD 由 GitHub 解析成該 repo 當下的預設分支。
        const string uri = "https://raw.githubusercontent.com/ffxiv-tc-port/NecroLens/HEAD/NecroLens/Data/allMobs.json";

        Task.Run(async () =>
        {
            List<MobInfo>? result = null;
            Exception? error = null;
            try
            {
                result = await Load<List<MobInfo>>(new Uri(uri)).ConfigureAwait(false);
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

    public static async Task<T?> Load<T>(Uri uri)
    {
        var result = await new HttpClient().GetAsync(uri).ConfigureAwait(true);
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
