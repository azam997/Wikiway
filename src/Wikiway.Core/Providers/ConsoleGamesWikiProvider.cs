using System.Globalization;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Core.Wiki;

namespace Wikiway.Core.Providers;

// Searches ffxiv.consolegameswiki.com. Each hit becomes a page link; for
// acquisition and unlock questions (and bare names) the top page's relevant
// sections are fetched too and shown as text. Only the search term is sent.
// Can be switched off in settings (IsAvailable).
public sealed class ConsoleGamesWikiProvider : ISearchProvider
{
    public const string ProviderId = "consolegameswiki";

    private readonly IWikiApiClient client;
    private readonly Func<bool> enabled;
    private readonly Func<int> maxResults;

    public ConsoleGamesWikiProvider(IWikiApiClient client, Func<bool>? enabled = null, Func<int>? maxResults = null)
    {
        this.client = client;
        this.enabled = enabled ?? (() => true);
        this.maxResults = maxResults ?? (() => 5);
    }

    public string Id => ProviderId;

    public bool IsAvailable => enabled();

    public async Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct)
    {
        var hits = await client.SearchAsync(query.Term, maxResults(), ct).ConfigureAwait(false);

        var results = new List<SearchResult>(hits.Count);
        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            var url = client.PageUrl(hit.Title);

            // The API returns relevance order; keep it, decaying gently. The
            // snippet is never shown, but a markup-dominated one flags a data
            // page that shouldn't outrank real articles.
            var score = Math.Max(0.1, 1.0 - (i * 0.08));
            if (hit.SnippetHtml.Length > 0 && WikiMarkup.IsMarkupDominated(HtmlText.Strip(hit.SnippetHtml)))
                score = Math.Min(score, 0.1);

            results.Add(new WikiPageResult
            {
                Title = hit.Title,
                Source = new Citation("consolegameswiki", url),
                PageUrl = url,
                Score = score,
            });
        }

        if (results.Count > 0 && results[0] is WikiPageResult best)
        {
            if (query.Intent == QueryIntent.Unlock &&
                string.Equals(
                    QueryNormalizer.StripLeadingArticle(best.Title),
                    QueryNormalizer.StripLeadingArticle(query.Term),
                    StringComparison.OrdinalIgnoreCase))
            {
                best = best with { Score = 1.0 };
                results[0] = best;
            }

            var sections = SectionExtractor.AppliesTo(query.Intent)
                ? await TryFetchSectionsAsync(query.Intent, best, ct).ConfigureAwait(false)
                : null;

            if (sections != null)
                results.Insert(0, sections);
        }

        return new ProviderResult(Id, results, ProviderStatus.Ok);
    }

    // Fetches the section list, picks up to three matching headings
    // (SectionExtractor), then fetches and cleans each section.
    private async Task<WikiSectionsResult?> TryFetchSectionsAsync(
        QueryIntent intent, WikiPageResult page, CancellationToken ct)
    {
        try
        {
            var sections = await client.GetSectionsAsync(page.Title, ct).ConfigureAwait(false);
            var picked = SectionExtractor.SelectSections(intent, sections);

            var bodies = new List<WikiSectionText>(picked.Count);
            foreach (var section in picked)
            {
                var html = await client.GetSectionHtmlAsync(
                        page.Title, int.Parse(section.Index, CultureInfo.InvariantCulture), ct)
                    .ConfigureAwait(false);
                if (html == null)
                    continue;

                var text = WikiMarkup.Clean(HtmlText.Strip(html));
                if (text.Length > 0)
                    bodies.Add(new WikiSectionText(section.Title, Truncate(text, 1200)));
            }

            if (bodies.Count == 0)
                return null;

            return new WikiSectionsResult
            {
                Title = page.Title,
                Source = page.Source,
                PageUrl = page.PageUrl,
                Sections = bodies,
                Score = 0.95,
            };
        }
        catch (HttpRequestException)
        {
            // Sections are extra detail on top of the page hit; a failed
            // section request here shouldn't fail the whole search.
            return null;
        }
        catch (OperationCanceledException)
        {
            // The token is the orchestrator's per-provider budget as much as
            // the caller's cancel, and after slow section fetches that budget
            // can run out with the page hits already in hand; letting it
            // escape reports the whole provider as timed out and discards
            // them. A caller cancel loses nothing: the response of a cancelled
            // search is never read.
            return null;
        }
    }

    // Cuts at a word boundary so a long section can't take over the window.
    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
            return text;

        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > 0 ? cut : max)] + " …";
    }
}
