using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace Wikiway.Plugin.GameIntegration;

internal sealed unsafe class QuestProgressTracker
{
    private const uint QuestRowBase = 0x10000;
    private const long StaleMs = 2000;

    private volatile Snapshot? snapshot;
    private long lastRefresh = long.MinValue;
    private List<uint>? questRowIds;

    public bool IsAvailable => snapshot != null;

    public bool IsComplete(uint questRowId) => snapshot is { } s && s.Completed.Contains(questRowId);

    public bool IsAccepted(uint questRowId) => snapshot is { } s && s.Accepted.Contains(questRowId);

    // Refresh only from the draw loop - UiBuilder.Draw runs on the game main
    // thread, the only place QuestManager may be read. Readers on any thread
    // see the immutable snapshot behind the volatile field.
    public void Refresh()
    {
        lastRefresh = Environment.TickCount64;
        var manager = QuestManager.Instance();
        if (!Plugin.ClientState.IsLoggedIn || manager == null)
        {
            snapshot = null;
            return;
        }

        questRowIds ??= Plugin.DataManager.GetExcelSheet<Quest>()!
            .Where(q => q.RowId is >= QuestRowBase and < QuestRowBase * 2 && !q.Name.IsEmpty)
            .Select(q => q.RowId)
            .ToList();

        var completed = new HashSet<uint>();
        foreach (var rowId in questRowIds)
        {
            if (QuestManager.IsQuestComplete((ushort)(rowId - QuestRowBase)))
                completed.Add(rowId);
        }

        var accepted = new HashSet<uint>();
        foreach (var quest in manager->NormalQuests)
        {
            if (quest.QuestId != 0)
                accepted.Add(QuestRowBase + quest.QuestId);
        }

        snapshot = new Snapshot(completed, accepted);
    }

    public void RefreshIfStale()
    {
        if (Environment.TickCount64 - lastRefresh >= StaleMs)
            Refresh();
    }

    private sealed record Snapshot(HashSet<uint> Completed, HashSet<uint> Accepted);
}
