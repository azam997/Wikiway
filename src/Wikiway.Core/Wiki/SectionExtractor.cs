using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Wiki;

public static class SectionExtractor
{
    private const int MaxSections = 3;

    // Substring stems, not exact headings - the wiki says "Obtained From",
    // "How to Obtain", "Dropped By" interchangeably depending on the page's
    // author. Purchase/crafting stems are deliberately absent: those sources
    // render from the game sheets, so the wiki only contributes what the
    // sheets can't know (drops, treasure hunts, exchanges).
    private static readonly string[] ItemKeywords =
    [
        "obtain", "source", "drop", "treasure", "desynth", "exchange", "gather", "reward",
    ];

    private static readonly string[] DutyKeywords =
    [
        "guide", "strategy", "walkthrough", "boss", "phase", "mechanic", "tips", "fight",
    ];

    public static IReadOnlyList<WikiSection> SelectSections(
        SearchCategory category, IReadOnlyList<WikiSection> sections)
    {
        var keywords = category switch
        {
            SearchCategory.Items => ItemKeywords,
            SearchCategory.Duties => DutyKeywords,
            _ => null,
        };
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
