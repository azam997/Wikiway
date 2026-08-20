using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Wikiway.Plugin.GameIntegration;

internal readonly record struct ActiveQuestEntry(string Name, ushort Level, bool Tracked);

internal static unsafe class ActiveQuestReader
{
    // Called from the draw loop only - UiBuilder.Draw runs on the game main
    // thread, so QuestManager can be read without marshalling.
    public static List<ActiveQuestEntry> Read()
    {
        var entries = new List<ActiveQuestEntry>();
        if (!Plugin.ClientState.IsLoggedIn)
            return entries;

        var manager = QuestManager.Instance();
        if (manager == null)
            return entries;

        var tracked = new HashSet<int>();
        foreach (var tracking in manager->TrackedQuests)
        {
            if (tracking.QuestType != 0)
                tracked.Add(tracking.Index);
        }

        var sheet = Plugin.DataManager.GetExcelSheet<Quest>()!;
        var quests = manager->NormalQuests;
        for (var i = 0; i < quests.Length; i++)
        {
            var quest = quests[i];
            if (quest.QuestId == 0 || quest.IsHidden)
                continue;

            // Journal quest ids are Quest sheet row ids minus the 0x10000 block base.
            var row = sheet.GetRowOrDefault(0x10000u + quest.QuestId);
            if (row == null || row.Value.Name.IsEmpty)
                continue;

            entries.Add(new ActiveQuestEntry(
                row.Value.Name.ExtractText(),
                row.Value.ClassJobLevel.FirstOrDefault(),
                tracked.Contains(i)));
        }

        entries.Sort((a, b) => a.Tracked != b.Tracked
            ? (a.Tracked ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }
}
