using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Wiki;

namespace Wikiway.Core.Pipeline;

// Folds wiki section results into the game-data card for the same subject, so
// one thing gets one row.
public static class ResultJoiner
{
    // A wiki sections result titled like an entity card is the same subject;
    // folding it into the card keeps one row per thing. Exact titles win, then
    // leading-article mismatches ("The Bozjan Southern Front" wiki page vs the
    // "Bozjan Southern Front" card) join only while unambiguous on both sides.
    // Provider counts are adjusted so the count strip still matches the rows
    // drawn. Only cards the headings were picked for can join
    // (SectionExtractor.Fits), so the Gold Saucer page's sections never land on
    // an NPC named Gold Saucer. For a bare name the sections were fetched as a
    // guess, so any that find no card are dropped rather than shown on their
    // own.
    public static (IReadOnlyList<SearchResult> Results, IReadOnlyList<ProviderResult> ProviderDetail) AttachWikiSections(
        IReadOnlyList<SearchResult> results, IReadOnlyList<ProviderResult> providerDetail, QueryIntent intent)
    {
        var dropUnjoined = intent == QueryIntent.Unknown;
        bool Joinable(EntityCardResult card) => SectionExtractor.Fits(intent, card.Entity);
        bool Dropped(WikiSectionsResult sections, HashSet<string> consumed) =>
            dropUnjoined || consumed.Contains(sections.Title);

        var sectionsByTitle = new Dictionary<string, WikiSectionsResult>(StringComparer.OrdinalIgnoreCase);
        var cardTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            if (result is WikiSectionsResult sections)
                sectionsByTitle.TryAdd(sections.Title, sections);
            else if (result is EntityCardResult card && Joinable(card))
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

        if (sectionsByCardTitle.Count == 0 && !dropUnjoined)
            return (results, providerDetail);

        var attached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new List<SearchResult>(results.Count);
        foreach (var result in results)
        {
            switch (result)
            {
                case WikiSectionsResult sections when Dropped(sections, consumed):
                    break;
                case EntityCardResult card when Joinable(card) &&
                    sectionsByCardTitle.TryGetValue(card.Title, out var source) && attached.Add(card.Title):
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
                    .Where(r => r is not WikiSectionsResult s || !Dropped(s, consumed))
                    .ToList(),
            })
            .ToList();

        return (output, detail);
    }

    // Second pass for titles that differ only by a leading "The"/"A"/"An".
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
