using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using NecroLens.Data;
using NecroLens.Model;

namespace NecroLens.util;

[SuppressMessage("ReSharper", "PatternIsRedundant")] // RSRP-492231
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class DeepDungeonUtil
{
    public static uint MapId => ClientState.TerritoryType;
    public static bool InDeepDungeon => InPotD || InHoH || InEO || InPT;
    public static bool InPotD => DataIds.PalaceOfTheDeadMapIds.Contains(MapId);
    public static bool InHoH => DataIds.HeavenOnHighMapIds.Contains(MapId);
    public static bool InEO => DataIds.EurekaOrthosMapIds.Contains(MapId);
    public static bool InPT => DataIds.PilgrimsTraverseMapIds.Contains(MapId);

    public static bool IsPomanderUsable(Pomander pomander)
    {
        // Only in Deep Dungeon of course :D
        var usable = InDeepDungeon;

        if (!usable)
        {
            PrintChatMessage(Strings.Pomander_NotInDeepDungeon);
            return false;
        }

        // checking for item penalty if not serenity
        if (pomander != Pomander.Serenity && pomander != Pomander.SerenityProtomander)
        {
            var itemPenalty = Player.Status.Where(s => s.StatusId == DataIds.ItemPenaltyStatusId);
            usable = usable && !itemPenalty.Any();
        }

        if (!usable)
        {
            PrintChatMessage(Strings.Pomander_ItemPenalty);
            return false;
        }

        usable = usable && pomander switch
        {
            // Normal Pomander can be used in PotD and HoH
            >= Pomander.Safety and <= Pomander.Serenity or Pomander.Intuition or Pomander.Raising => InPotD || InHoH || InPT,

            // PotD exclusive Pomander
            Pomander.Rage or Pomander.Lust or Pomander.Resolution => InPotD,

            // Eureka exclusive Pomander
            Pomander.Frailty or Pomander.Concealment or Pomander.Petrification => InHoH,

            // Protomander can be used in EO only
            >= Pomander.LethargyProtomander and <= Pomander.RaisingProtomander => InEO,

            >= Pomander.HastePomander and <= Pomander.DevotionPomander => InPT,

            _ => false
        };

        if (!usable)
        {
            var name = DungeonService.PomanderNames.GetValueOrDefault(pomander, pomander.ToString());
            PrintChatMessage(string.Format(Strings.Pomander_NotInThisDungeon, name));
            return false;
        }

        return usable;
    }

    public static bool TryFindPomanderByName(string name, out Pomander pomander)
    {
        pomander = default;
        if (name.IsNullOrEmpty())
        {
            PrintChatMessage(Strings.Pomander_NeedName);
            return false;
        }

        var sheet = DataManager.GetExcelSheet<Lumina.Excel.Sheets.DeepDungeonItem>()!;
        // TC 的 DeepDungeonItem 把名稱放在 Name，Singular 欄位 39 列全是空字串
        // （已對實機 sqpack 查證）。原本比對 Singular，在台服永遠找不到任何魔陶器，
        // 等於 /pomander <名稱> 整個指令失效。改用 Name，其他語系的 Name 也有值。
        var matches = sheet.Where(e => e.RowId is > 0 and < 23 or > 36)
                           .Where(e => e.Name.ToString().Contains(name, StringComparison.OrdinalIgnoreCase))
                           .ToList();

        if (matches.Count > 1)
        {
            PrintChatMessage(string.Format(Strings.Pomander_MultipleMatches, name));
        }
        else if (!matches.Any())
        {
            // Nothing found? Try match with enum
            if (!Enum.TryParse(name, true, out pomander))
            {
                PrintChatMessage(string.Format(Strings.Pomander_NoMatches, name));
            }
        }
        else
        {
            pomander = (Pomander)matches.First().RowId;
        }

        // if we are in EO and use normal names we have to shift them
        if (InEO)
        {
            if (pomander is >= Pomander.Safety and <= Pomander.Serenity)
            {
                pomander += 22;
            }

            if (pomander is Pomander.Intuition or Pomander.Raising)
            {
                pomander += 20;
            }
        }

        return pomander != default;
    }


    public static void PrintChatMessage(string msg)
    {
        var message = new XivChatEntry
        {
            Message = new SeStringBuilder()
                      .AddUiForeground($"[NecroLens] ", 48)
                      .Append(msg).Build()
        };

        Svc.Chat.Print(message);
    }
}
