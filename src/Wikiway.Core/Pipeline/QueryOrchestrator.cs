using System.Diagnostics;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

// Runs one search: normalize the text, query every provider in parallel (each
// under its own timeout), rank the combined results, then fold matching wiki
// sections into game-data cards. Runs on the thread pool.
public sealed class QueryOrchestrator : IQueryPipeline
{
    private readonly IReadOnlyList<ISearchProvider> providers;
    private readonly QueryNormalizer normalizer;
    private readonly ResultRanker ranker;
    private readonly TimeSpan providerTimeout;

    public QueryOrchestrator(
        IReadOnlyList<ISearchProvider> providers,
        QueryNormalizer normalizer,
        ResultRanker ranker,
        TimeSpan? providerTimeout = null)
    {
        this.providers = providers;
        this.normalizer = normalizer;
        this.ranker = ranker;
        this.providerTimeout = providerTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<QueryResponse> ExecuteAsync(string rawQuery, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var query = normalizer.Normalize(rawQuery);

        // Each provider gets its own pool task. Called directly, the local
        // provider's await on its finished index completes synchronously, so
        // its whole search would run before the wiki request even started.
        var searches = providers
            .Select(p => p.IsAvailable
                ? Task.Run(() => RunProviderAsync(p, query, ct))
                : Task.FromResult(ProviderResult.Skip(p.Id)))
            .ToList();

        var providerResults = await Task.WhenAll(searches).ConfigureAwait(false);
        var merged = ranker.Merge(query, providerResults);
        var (results, detail) = ResultJoiner.AttachWikiSections(merged, providerResults, query.Intent);

        return new QueryResponse(query, results, detail, watch.Elapsed);
    }

    // A provider that times out or throws becomes a Failure entry instead of
    // failing the query; only the caller's own cancellation propagates.
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
}
