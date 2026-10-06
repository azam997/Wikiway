using Lumina.Excel.Sheets;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;

namespace Wikiway.GameData;

// Quests: the card, its prerequisites, and the unlock chains walked from them.
public sealed partial class LuminaGameDataStore
{
    // The quest plus its unlock chains, walked back through prerequisites by
    // QuestChainWalker (Core).
    public QuestEntity? GetQuest(uint rowId)
    {
        var quest = BuildQuest(rowId);
        if (quest is not { Prerequisites.Count: > 0 })
            return quest;

        var (chains, msqVersion) = QuestChainWalker.Walk(quest, BuildQuest);
        return quest with { UnlockChains = chains, MsqRequirement = msqVersion };
    }

    // One Quest row without the chain walk; the walker calls this for every
    // step. Expansion row + 2 is the major patch (Dawntrail = 5 -> "7.x").
    private QuestEntity? BuildQuest(uint rowId)
    {
        var row = Sheet<Quest>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var quest = row.Value;
        var prerequisites = new List<QuestLink>();
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && previous.ValueNullable is { } prev && !prev.Name.IsEmpty)
                prerequisites.Add(new QuestLink(previous.RowId, prev.Name.ExtractText()));
        }

        var genre = quest.JournalGenre.ValueNullable;
        var category = genre?.JournalCategory.ValueNullable?.Name.ExtractText() ?? "";

        return new QuestEntity(
            rowId,
            quest.Name.ExtractText(),
            quest.ClassJobLevel.FirstOrDefault(),
            genre?.Name.ExtractText() ?? "",
            prerequisites)
        {
            PrerequisiteJoin = quest.PreviousQuestJoin == 2 ? QuestJoin.Any
                : prerequisites.Count > 0 ? QuestJoin.All : QuestJoin.None,
            Expansion = $"{quest.Expansion.RowId + 2}.x",
            MainScenario = IsMainScenario(category),
            StartLocation = quest.IssuerLocation.ValueNullable is { } issuer ? ToMapLocation(issuer) : null,
        };
    }

    // MSQ is identified by the journal category name, because the genre alone
    // is just the expansion ("Dawntrail").
    private bool IsMainScenario(uint questRowId)
    {
        var quest = Sheet<Quest>().GetRowOrDefault(questRowId);
        return IsMainScenario(
            quest?.JournalGenre.ValueNullable?.JournalCategory.ValueNullable?.Name.ExtractText() ?? "");
    }

    private static bool IsMainScenario(string journalCategory) =>
        journalCategory.Contains("Main Scenario", StringComparison.OrdinalIgnoreCase);
}
