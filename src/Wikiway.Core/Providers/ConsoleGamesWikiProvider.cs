using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Wiki;

namespace Wikiway.Core.Providers;

public sealed class ConsoleGamesWikiProvider : ISearchProvider, IDocumentRetriever
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

            // The API returns relevance order; keep it, decaying gently.
            var score = Math.Max(0.1, 1.0 - (i * 0.08));

            string? snippet = null;
            if (hit.SnippetHtml.Length > 0)
            {
                var raw = HtmlText.Strip(hit.SnippetHtml);
                if (WikiMarkup.IsMarkupDominated(raw))
                    score = Math.Min(score, 0.1);
                else
                    snippet = WikiMarkup.Clean(raw);
            }

            results.Add(new WikiPageResult
            {
                Title = hit.Title,
                Source = new Citation("consolegameswiki", url),
                PageUrl = url,
                Snippet = snippet is { Length: > 0 } ? snippet : null,
                Score = score,
            });
        }

        if (results.Count > 0 && results[0] is WikiPageResult best)
        {
            if (query.Category == SearchCategory.Quests &&
                string.Equals(best.Title, query.Term, StringComparison.OrdinalIgnoreCase))
            {
                best = best with { Score = 1.0 };
                results[0] = best;
            }

            var sections = query.Category is SearchCategory.Items or SearchCategory.Duties
                ? await TryFetchSectionsAsync(query.Category, best, ct).ConfigureAwait(false)
                : null;

            if (sections != null)
                results.Insert(0, sections);
            else
                await TryAddLeadAsync(results, best, ct).ConfigureAwait(false);
        }

        return new ProviderResult(Id, results, ProviderStatus.Ok);
    }

    private async Task TryAddLeadAsync(List<SearchResult> results, WikiPageResult best, CancellationToken ct)
    {
        try
        {
            var lead = await client.GetLeadSectionHtmlAsync(best.Title, ct).ConfigureAwait(false);
            if (lead != null)
            {
                var raw = HtmlText.Strip(lead);
                if (WikiMarkup.IsMarkupDominated(raw))
                {
                    results[0] = best with { Score = Math.Min(best.Score, 0.1) };
                }
                else
                {
                    var text = WikiMarkup.Clean(raw);
                    if (text.Length > 0)
                        results[0] = best with { Lead = Truncate(text, 700) };
                }
            }
        }
        catch (HttpRequestException)
        {
            // The lead paragraph is a bonus; the hit stands on its own.
        }
    }

    private async Task<WikiSectionsResult?> TryFetchSectionsAsync(
        SearchCategory category, WikiPageResult page, CancellationToken ct)
    {
        try
        {
            var sections = await client.GetSectionsAsync(page.Title, ct).ConfigureAwait(false);
            var picked = SectionExtractor.SelectSections(category, sections);

            var bodies = new List<WikiSectionText>(picked.Count);
            foreach (var section in picked)
            {
                var html = await client.GetSectionHtmlAsync(page.Title, int.Parse(section.Index), ct)
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
            return null;
        }
    }

    private static string Truncate(string text, int max)
    {
        if (text.Length <= max)
            return text;

        var cut = text.LastIndexOf(' ', max);
        return text[..(cut > 0 ? cut : max)] + " …";
    }

    public async Task<RetrievedDocument?> RetrieveAsync(SearchResult hit, CancellationToken ct)
    {
        if (hit is not WikiPageResult page)
            return null;

        var text = await client.GetPagePlainTextAsync(page.Title, ct).ConfigureAwait(false);
        return text == null ? null : new RetrievedDocument(page.Title, page.PageUrl, text);
    }
}
