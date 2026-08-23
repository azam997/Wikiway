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
    // Not long.MinValue: TickCount64 minus that overflows negative, and the
    // stale check would never fire until the first forced Refresh.
    private long lastRefresh = -StaleMs;
    private List<uint>? questRowIds;
    private bool warnedUnresolved;

    public bool IsAvailable => snapshot != null;

    public bool IsComplete(uint questRowId) => snapshot is { } s && s.Completed.Contains(questRowId);

    public bool IsAccepted(uint questRowId) => snapshot is { } s && s.Accepted.Contains(questRowId);

    // Refresh only from the draw loop - UiBuilder.Draw runs on the game main
    // thread, the only place QuestManager may be read. Readers on any thread
    // see the immutable snapshot behind the volatile field.
    public void Refresh()
    {
        lastRefresh = Environment.TickCount64;

        // IsQuestComplete below is a [MemberFunction] native call through a
        // generated pointer with no null guard of its own: if the signature
        // stops resolving after a game patch, calling it is a jump to address
        // 0 - an uncatchable client crash. A null snapshot fails gating open.
        if (QuestManager.Addresses.IsQuestComplete.Value == IntPtr.Zero)
        {
            if (!warnedUnresolved)
            {
                warnedUnresolved = true;
                Plugin.Log.Warning(
                    "QuestManager.IsQuestComplete signature did not resolve on this game build; " +
                    "quest progress (spoiler gating, chain checkmarks) is disabled until the plugin is updated.");
            }

            snapshot = null;
            return;
        }

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

        // IsQuestComplete reads the client's completed-quest bitmap, indexed
        // by the ushort id the game uses everywhere outside exd; the Quest
        // sheet keys those rows at 0x10000 + id, hence the subtraction.
        // questRowIds is pre-filtered to that block, so the cast can't fold a
        // different event type's row onto a quest id. The static is missing
        // from the ClientStructs XML docs; verified present and working on
        // this build by reflection over the shipped assembly and in-game.
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
