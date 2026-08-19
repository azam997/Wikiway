using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class ResultJoiner
{
    // A wiki sections result titled exactly like an entity card is the same
    // subject; folding it into the card keeps one row per thing. Provider
    // counts are adjusted so the count strip still matches the rows drawn.
    public static (IReadOnlyList<SearchResult> Results, IReadOnlyList<ProviderResult> ProviderDetail) AttachWikiSections(
        IReadOnlyList<SearchResult> results, IReadOnlyList<ProviderResult> providerDetail)
    {
        var sectionsByTitle = new Dictionary<string, WikiSectionsResult>(StringComparer.OrdinalIgnoreCase);
        var cardTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result is WikiSectionsResult sections)
                sectionsByTitle.TryAdd(sections.Title, sections);
            else if (result is EntityCardResult card)
                cardTitles.Add(card.Title);
        }

        var joined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in sectionsByTitle.Keys)
        {
            if (cardTitles.Contains(title))
                joined.Add(title);
        }

        if (joined.Count == 0)
            return (results, providerDetail);

        var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<SearchResult>(results.Count);
        foreach (var result in results)
        {
            switch (result)
            {
                case WikiSectionsResult sections when joined.Contains(sections.Title):
                    break;
                case EntityCardResult card when joined.Contains(card.Title) && attached.Add(card.Title):
                    var source = sectionsByTitle[card.Title];
                    output.Add(card with { WikiSections = source.Sections, WikiUrl = source.PageUrl });
                    break;
                default:
                    output.Add(result);
                    break;
            }
        }

        var detail = providerDetail
            .Select(p => p with
            {
                Results = p.Results
                    .Where(r => r is not WikiSectionsResult s || !joined.Contains(s.Title))
                    .ToList(),
            })
            .ToList();

        return (output, detail);
    }
}
