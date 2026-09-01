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

    public static IReadOnlyList<WikiSection> SelectSections(
        SearchCategory category, IReadOnlyList<WikiSection> sections)
    {
        var keywords = category switch
        {
            SearchCategory.Items or SearchCategory.Gathering => ItemKeywords,
            SearchCategory.Unlocks => UnlockKeywords,
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
