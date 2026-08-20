using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;
using static Wikiway.Core.Tests.TestData;

namespace Wikiway.Core.Tests;

public class QueryOrchestratorTests
{
    private static QueryOrchestrator Build(
        IReadOnlyList<ISearchProvider> providers,
        TimeSpan? timeout = null)
        => new(providers, new QueryNormalizer(), new ResultRanker(), timeout);

    [Fact]
    public async Task MergesResultsFromAllProviders()
    {
        var orchestrator = Build([
            new StubProvider("a", [Card(Npc(), 1.0)]),
            new StubProvider("b", [WikiResult("Momodi", 0.9)]),
        ]);

        var result = await orchestrator.ExecuteAsync("momodi", SearchCategory.Other, CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
    }

    [Fact]
    public async Task ThrowingProviderBecomesFailedStatusAndOthersSurvive()
    {
        var orchestrator = Build([
            new StubProvider("boom", _ => throw new InvalidOperationException("no")),
            new StubProvider("ok", [WikiResult("Momodi", 0.9)]),
        ]);

        var result = await orchestrator.ExecuteAsync("momodi", SearchCategory.Other, CancellationToken.None);

        Assert.Single(result.Results);
        var failed = result.ProviderDetail.Single(p => p.ProviderId == "boom");
        Assert.Equal(ProviderStatus.Failed, failed.Status);
        Assert.Equal("no", failed.Error);
    }

    [Fact]
    public async Task UnavailableProviderIsSkippedNotInvoked()
    {
        var invoked = false;
        var provider = new StubProvider("off", _ =>
        {
            invoked = true;
            return Task.FromResult(new ProviderResult("off", [], ProviderStatus.Ok));
        })
        { Available = false };

        var result = await Build([provider]).ExecuteAsync("momodi", SearchCategory.Other, CancellationToken.None);

        Assert.False(invoked);
        Assert.Equal(ProviderStatus.Skipped, result.ProviderDetail.Single().Status);
    }

    [Fact]
    public async Task SlowProviderTimesOutAsFailed()
    {
        var slow = new StubProvider("slow", async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new ProviderResult("slow", [], ProviderStatus.Ok);
        });

        var orchestrator = Build([slow], timeout: TimeSpan.FromMilliseconds(50));
        var result = await orchestrator.ExecuteAsync("momodi", SearchCategory.Other, CancellationToken.None);

        Assert.Equal(ProviderStatus.Failed, result.ProviderDetail.Single().Status);
    }

    [Fact]
    public async Task CallerCancellationPropagates()
    {
        using var cts = new CancellationTokenSource();
        var slow = new StubProvider("slow", async ct =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new ProviderResult("slow", [], ProviderStatus.Ok);
        });

        var task = Build([slow]).ExecuteAsync("momodi", SearchCategory.Other, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task CategoryReachesProviders()
    {
        var provider = new StubProvider("spy", []);

        await Build([provider]).ExecuteAsync("iron ingot", SearchCategory.Items, CancellationToken.None);

        Assert.Equal(SearchCategory.Items, provider.LastQuery!.Category);
        Assert.Equal(QueryIntent.Acquisition, provider.LastQuery.Intent);
    }

    [Fact]
    public async Task ElapsedTimeIsMeasured()
    {
        var slow = new StubProvider("slow", async ct =>
        {
            await Task.Delay(10, ct);
            return new ProviderResult("slow", [], ProviderStatus.Ok);
        });

        var result = await Build([slow]).ExecuteAsync("momodi", SearchCategory.Other, CancellationToken.None);

        Assert.True(result.Elapsed > TimeSpan.Zero);
    }

    private sealed class StubProvider : ISearchProvider
    {
        private readonly Func<CancellationToken, Task<ProviderResult>> impl;

        public StubProvider(string id, IReadOnlyList<SearchResult> results)
        {
            Id = id;
            impl = _ => Task.FromResult(new ProviderResult(id, results, ProviderStatus.Ok));
        }

        public StubProvider(string id, Func<CancellationToken, Task<ProviderResult>> impl)
        {
            Id = id;
            this.impl = impl;
        }

        public string Id { get; }
        public bool Available { get; init; } = true;
        public bool IsAvailable => Available;
        public NormalizedQuery? LastQuery { get; private set; }

        public Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct)
        {
            LastQuery = query;
            return impl(ct);
        }
    }
}
