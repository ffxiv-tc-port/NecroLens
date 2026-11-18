using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace NecroLens.util;

/// <summary>
/// 呼叫 FFXIVClientStructs 成員函式／<c>Instance()</c> 之前先判位址。
/// 改版後特徵碼對不上時 CS 會擲 <see cref="System.InvalidOperationException"/>：每幀路徑上等於每幀一次例外＋一行 Error，
/// 而且擲在 ESP 掃描中途會讓那一 tick 的疊加層整個不更新。對不上＝回 false、呼叫端停用該功能，每個位址只記一次。
/// </summary>
internal static unsafe class GameFunctions
{
    private static readonly HashSet<string> Reported = new();

    public static bool Resolved(void* address, [CallerArgumentExpression(nameof(address))] string name = "")
    {
        if (address != null) return true;

        lock (Reported)
        {
            if (Reported.Add(name))
                PluginLog.Warning($"遊戲函式位址對不上（{name}），相關功能停用或改用備援判斷；需等插件更新。");
        }

        return false;
    }
}
