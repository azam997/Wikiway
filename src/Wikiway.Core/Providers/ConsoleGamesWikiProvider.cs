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
            results.Add(new WikiPageResult
            {
                Title = hit.Title,
                Source = new Citation("consolegameswiki", url),
                PageUrl = url,
                Snippet = hit.SnippetHtml.Length > 0 ? HtmlText.Strip(hit.SnippetHtml) : null,
                // The API returns relevance order; keep it, decaying gently.
                Score = Math.Max(0.1, 1.0 - (i * 0.08)),
            });
        }

        return new ProviderResult(Id, results, ProviderStatus.Ok);
    }

    public async Task<RetrievedDocument?> RetrieveAsync(SearchResult hit, CancellationToken ct)
    {
        if (hit is not WikiPageResult page)
            return null;

        var text = await client.GetPagePlainTextAsync(page.Title, ct).ConfigureAwait(false);
        return text == null ? null : new RetrievedDocument(page.Title, page.PageUrl, text);
    }
}
