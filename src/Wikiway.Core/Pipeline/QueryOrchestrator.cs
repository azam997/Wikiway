using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public sealed class QueryOrchestrator : IQueryPipeline
{
    private readonly IReadOnlyList<ISearchProvider> providers;
    private readonly QueryNormalizer normalizer;
    private readonly ResultRanker ranker;
    private readonly IDocumentRetriever? retriever;
    private readonly IAnswerSynthesizer? synthesizer;
    private readonly TimeSpan providerTimeout;

    public QueryOrchestrator(
        IReadOnlyList<ISearchProvider> providers,
        QueryNormalizer normalizer,
        ResultRanker ranker,
        IDocumentRetriever? retriever = null,
        IAnswerSynthesizer? synthesizer = null,
        TimeSpan? providerTimeout = null)
    {
        this.providers = providers;
        this.normalizer = normalizer;
        this.ranker = ranker;
        this.retriever = retriever;
        this.synthesizer = synthesizer;
        this.providerTimeout = providerTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<QueryResponse> ExecuteAsync(string rawQuery, CancellationToken ct)
    {
        var query = normalizer.Normalize(rawQuery);

        var searches = providers
            .Select(p => p.IsAvailable
                ? RunProviderAsync(p, query, ct)
                : Task.FromResult(ProviderResult.Skip(p.Id)))
            .ToList();

        var providerResults = await Task.WhenAll(searches).ConfigureAwait(false);
        var merged = ranker.Merge(query, providerResults);
        var answer = await TrySynthesizeAsync(query, merged, ct).ConfigureAwait(false);

        return new QueryResponse(query, merged, providerResults, answer);
    }

    private async Task<ProviderResult> RunProviderAsync(
        ISearchProvider provider, NormalizedQuery query, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(providerTimeout);

        try
        {
            return await provider.SearchAsync(query, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return ProviderResult.Failure(provider.Id, "timed out");
        }
        catch (Exception e)
        {
            // One misbehaving provider shouldn't take the whole query down.
            return ProviderResult.Failure(provider.Id, e.Message);
        }
    }

    private async Task<SynthesizedAnswer?> TrySynthesizeAsync(
        NormalizedQuery query, IReadOnlyList<SearchResult> results, CancellationToken ct)
    {
        if (synthesizer is not { IsConfigured: true } || retriever is null)
            return null;

        var documents = new List<RetrievedDocument>();
        foreach (var hit in results.OfType<WikiPageResult>().Take(3))
        {
            var doc = await retriever.RetrieveAsync(hit, ct).ConfigureAwait(false);
            if (doc != null)
                documents.Add(doc);
        }

        if (documents.Count == 0)
            return null;

        return await synthesizer.SynthesizeAsync(query, documents, ct).ConfigureAwait(false);
    }
}
