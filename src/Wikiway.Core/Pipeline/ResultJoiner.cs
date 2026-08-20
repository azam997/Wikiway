using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class ResultJoiner
{
    // A wiki sections result titled like an entity card is the same subject;
    // folding it into the card keeps one row per thing. Exact titles win, then
    // leading-article mismatches ("The Bozjan Southern Front" wiki page vs the
    // "Bozjan Southern Front" card) join only while unambiguous on both sides.
    // Provider counts are adjusted so the count strip still matches the rows drawn.
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

        var sectionsByCardTitle = new Dictionary<string, WikiSectionsResult>(StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (title, sections) in sectionsByTitle)
        {
            if (cardTitles.Contains(title))
            {
                sectionsByCardTitle[title] = sections;
                consumed.Add(title);
            }
        }

        JoinArticleStripped(sectionsByTitle, cardTitles, sectionsByCardTitle, consumed);

        if (sectionsByCardTitle.Count == 0)
            return (results, providerDetail);

        var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<SearchResult>(results.Count);
        foreach (var result in results)
        {
            switch (result)
            {
                case WikiSectionsResult sections when consumed.Contains(sections.Title):
                    break;
                case EntityCardResult card when sectionsByCardTitle.TryGetValue(card.Title, out var source) && attached.Add(card.Title):
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
                    .Where(r => r is not WikiSectionsResult s || !consumed.Contains(s.Title))
                    .ToList(),
            })
            .ToList();

        return (output, detail);
    }

    private static void JoinArticleStripped(
        Dictionary<string, WikiSectionsResult> sectionsByTitle,
        HashSet<string> cardTitles,
        Dictionary<string, WikiSectionsResult> sectionsByCardTitle,
        HashSet<string> consumed)
    {
        // Null marks a stripped key claimed by more than one title; collisions
        // must not join, or two different subjects would swap content.
        var strippedCards = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var title in cardTitles)
        {
            if (sectionsByCardTitle.ContainsKey(title))
                continue;
            var key = QueryNormalizer.StripLeadingArticle(title);
            strippedCards[key] = strippedCards.ContainsKey(key) ? null : title;
        }

        var strippedSections = new Dictionary<string, WikiSectionsResult?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (title, sections) in sectionsByTitle)
        {
            if (consumed.Contains(title))
                continue;
            var key = QueryNormalizer.StripLeadingArticle(title);
            strippedSections[key] = strippedSections.ContainsKey(key) ? null : sections;
        }

        foreach (var (key, sections) in strippedSections)
        {
            if (sections == null || !strippedCards.TryGetValue(key, out var cardTitle) || cardTitle == null)
                continue;
            sectionsByCardTitle[cardTitle] = sections;
            consumed.Add(sections.Title);
        }
    }
}
