using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Providers;
using Xunit;

namespace Wikiway.Core.Tests;

public class WikiProviderTests
{
    private static NormalizedQuery Query(string term) => new(term, term, QueryIntent.Unknown);

    [Fact]
    public async Task MapsHitsToWikiResultsInRelevanceOrder()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("First", 1, "about <span class=\"searchmatch\">first</span>"),
            new WikiSearchHit("Second", 2, "")));

        var result = await provider.SearchAsync(Query("first"), CancellationToken.None);

        Assert.Equal(ProviderStatus.Ok, result.Status);
        Assert.Equal(2, result.Results.Count);

        var first = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.Equal("First", first.Title);
        Assert.Equal("about first", first.Snippet);
        Assert.True(first.Score > result.Results[1].Score);
        Assert.Contains("consolegameswiki.com/wiki/First", first.PageUrl.ToString());
    }

    [Fact]
    public void DisabledInConfigMeansUnavailable()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(), enabled: () => false);

        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public async Task RetrieverFetchesPlainTextForWikiHits()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient());
        var hit = new WikiPageResult
        {
            Title = "Aether Currents",
            Source = new Citation("wiki"),
            PageUrl = new Uri("https://example.com"),
        };

        var doc = await provider.RetrieveAsync(hit, CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal("plain text of Aether Currents", doc.PlainText);
    }

    [Fact]
    public async Task RetrieverIgnoresNonWikiResults()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient());

        var doc = await provider.RetrieveAsync(TestData.Card(TestData.Npc(), 1.0), CancellationToken.None);

        Assert.Null(doc);
    }

    private sealed class StubWikiClient(params WikiSearchHit[] hits) : IWikiApiClient
    {
        public Uri PageUrl(string title) =>
            new("https://ffxiv.consolegameswiki.com/wiki/" + title.Replace(' ', '_'));

        public Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WikiSearchHit>>(hits);

        public Task<string?> GetLeadSectionHtmlAsync(string pageTitle, CancellationToken ct) =>
            Task.FromResult<string?>($"<p>lead of {pageTitle}</p>");

        public Task<string?> GetPagePlainTextAsync(string pageTitle, CancellationToken ct) =>
            Task.FromResult<string?>($"plain text of {pageTitle}");
    }
}
