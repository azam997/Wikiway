using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Wiki;

public static class SectionExtractor
{
    private const int MaxSections = 3;

    // Substring stems, not exact headings - the wiki says "Obtained From",
    // "How to Obtain", "Dropped By" interchangeably depending on the page's
    // author, and machine-generated item pages put drops under a "Duties"
    // subsection. Purchase/crafting/exchange/gathering stems are deliberately
    // absent: those sources render from the game sheets, so the wiki only
    // contributes what the sheets can't know (drops, treasure hunts).
    private static readonly string[] ItemKeywords =
    [
        "obtain", "source", "drop", "duty", "duties", "treasure", "desynth", "reward",
    ];

    private static readonly string[] UnlockKeywords =
    [
        "unlock", "access", "requirement", "entry", "getting started", "overview",
    ];

    // A bare name is most often an item, so it gets the item stems on spec;
    // ResultJoiner drops those sections unless they land on an item card.
    private static string[]? KeywordsFor(QueryIntent intent) => intent switch
    {
        QueryIntent.Acquisition or QueryIntent.Unknown => ItemKeywords,
        QueryIntent.Unlock => UnlockKeywords,
        _ => null,
    };

    public static bool AppliesTo(QueryIntent intent) => KeywordsFor(intent) != null;

    // The cards whose subject those same keywords describe.
    public static bool Fits(QueryIntent intent, GameEntity entity) => intent switch
    {
        QueryIntent.Acquisition or QueryIntent.Unknown => entity is ItemEntity,
        QueryIntent.Unlock => entity is QuestEntity or DutyEntity,
        _ => false,
    };

    public static IReadOnlyList<WikiSection> SelectSections(
        QueryIntent intent, IReadOnlyList<WikiSection> sections)
    {
        var keywords = KeywordsFor(intent);
        if (keywords == null)
            return [];

        var picked = new List<WikiSection>();
        foreach (var section in sections)
        {
            if (!int.TryParse(section.Index, out _))
                continue;

            if (keywords.Any(k => section.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                picked.Add(section);
                if (picked.Count == MaxSections)
                    break;
            }
        }

        return picked;
    }
}
