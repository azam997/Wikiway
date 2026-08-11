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
        IDocumentRetriever? retriever = null,
        IAnswerSynthesizer? synthesizer = null,
        TimeSpan? timeout = null)
        => new(providers, new QueryNormalizer(), new ResultRanker(), retriever, synthesizer, timeout);

    [Fact]
    public async Task MergesResultsFromAllProviders()
    {
        var orchestrator = Build([
            new StubProvider("a", [Card(Npc(), 1.0)]),
            new StubProvider("b", [WikiResult("Momodi", 0.9)]),
        ]);

        var result = await orchestrator.ExecuteAsync("momodi", CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
    }

    [Fact]
    public async Task ThrowingProviderBecomesFailedStatusAndOthersSurvive()
    {
        var orchestrator = Build([
            new StubProvider("boom", _ => throw new InvalidOperationException("no")),
            new StubProvider("ok", [WikiResult("Momodi", 0.9)]),
        ]);

        var result = await orchestrator.ExecuteAsync("momodi", CancellationToken.None);

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

        var result = await Build([provider]).ExecuteAsync("momodi", CancellationToken.None);

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
        var result = await orchestrator.ExecuteAsync("momodi", CancellationToken.None);

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

        var task = Build([slow]).ExecuteAsync("momodi", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task NoSynthesizerMeansNullAnswer()
    {
        var orchestrator = Build([new StubProvider("wiki", [WikiResult("Momodi", 0.9)])]);

        var result = await orchestrator.ExecuteAsync("momodi", CancellationToken.None);

        Assert.Null(result.Answer);
    }

    [Fact]
    public async Task ConfiguredSynthesizerGetsRetrievedDocs()
    {
        var orchestrator = Build(
            [new StubProvider("wiki", [WikiResult("Momodi", 0.9)])],
            new StubRetriever(),
            new StubSynthesizer());

        var result = await orchestrator.ExecuteAsync("momodi", CancellationToken.None);

        Assert.NotNull(result.Answer);
        Assert.Contains("Momodi", result.Answer!.Text);
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

        public Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct) => impl(ct);
    }

    private sealed class StubRetriever : IDocumentRetriever
    {
        public Task<RetrievedDocument?> RetrieveAsync(SearchResult hit, CancellationToken ct) =>
            Task.FromResult<RetrievedDocument?>(
                new RetrievedDocument(hit.Title, new Uri("https://example.com"), $"Text about {hit.Title}"));
    }

    private sealed class StubSynthesizer : IAnswerSynthesizer
    {
        public bool IsConfigured => true;

        public Task<SynthesizedAnswer?> SynthesizeAsync(
            NormalizedQuery query, IReadOnlyList<RetrievedDocument> documents, CancellationToken ct) =>
            Task.FromResult<SynthesizedAnswer?>(
                new SynthesizedAnswer($"Answer from {documents[0].Title}", []));
    }
}
