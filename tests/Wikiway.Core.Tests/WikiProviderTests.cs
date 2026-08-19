using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Providers;
using Xunit;

namespace Wikiway.Core.Tests;

public class WikiProviderTests
{
    private static NormalizedQuery Query(string term, SearchCategory category = SearchCategory.Other) =>
        new(term, term, QueryIntent.Unknown, category);

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
    public async Task TopHitGetsALeadParagraph()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("First", 1, ""),
            new WikiSearchHit("Second", 2, "")));

        var result = await provider.SearchAsync(Query("first"), CancellationToken.None);

        var first = Assert.IsType<WikiPageResult>(result.Results[0]);
        var second = Assert.IsType<WikiPageResult>(result.Results[1]);
        Assert.Equal("lead of First", first.Lead);
        Assert.Null(second.Lead);
    }

    [Fact]
    public async Task ItemsCategoryEmitsSectionsFromMatchingHeadings()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(new WikiSearchHit("Iron Ingot", 1, ""))
        {
            Sections = [new WikiSection("2", "Treasure Hunt", 1), new WikiSection("5", "Used For", 1)],
        });

        var result = await provider.SearchAsync(Query("iron ingot", SearchCategory.Items), CancellationToken.None);

        var sections = Assert.IsType<WikiSectionsResult>(result.Results[0]);
        var section = Assert.Single(sections.Sections);
        Assert.Equal("Treasure Hunt", section.Heading);
        Assert.Contains("section 2 of Iron Ingot", section.Text);
        Assert.IsType<WikiPageResult>(result.Results[1]);
    }

    [Fact]
    public async Task NoMatchingSectionsFallsBackToLead()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(new WikiSearchHit("Iron Ingot", 1, ""))
        {
            Sections = [new WikiSection("1", "Lore", 1)],
        });

        var result = await provider.SearchAsync(Query("iron ingot", SearchCategory.Items), CancellationToken.None);

        var first = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.Equal("lead of Iron Ingot", first.Lead);
    }

    [Fact]
    public async Task QuestsCategoryBoostsExactTitleToFullScore()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("The Ultimate Weapon", 1, "")));

        var result = await provider.SearchAsync(
            Query("the ultimate weapon", SearchCategory.Quests), CancellationToken.None);

        Assert.Equal(1.0, result.Results[0].Score);
    }

    [Fact]
    public async Task RedirectSnippetIsDroppedAndDemoted()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("Momodi Modi", 1, "#REDIRECT [[Momodi]]")));

        var result = await provider.SearchAsync(Query("momodi"), CancellationToken.None);

        var hit = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.Null(hit.Snippet);
        Assert.True(hit.Score <= 0.1);
    }

    [Fact]
    public async Task MarkupDominatedLeadIsSkippedAndDemoted()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(new WikiSearchHit("Momodi", 1, "prose snippet"))
        {
            LeadHtml = "<style>.infobox{float:right}</style>{{npcbox|name=Momodi}}",
        });

        var result = await provider.SearchAsync(Query("momodi"), CancellationToken.None);

        var hit = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.Null(hit.Lead);
        Assert.True(hit.Score <= 0.1);
    }

    [Fact]
    public async Task InfoboxSourceSnippetIsDroppedAndDemoted()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("Momodi", 1, "| name = Momodi\n| full-name = Momodi Modi")));

        var result = await provider.SearchAsync(Query("momodi"), CancellationToken.None);

        var hit = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.Null(hit.Snippet);
        Assert.True(hit.Score <= 0.1);
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
        public IReadOnlyList<WikiSection> Sections { get; init; } = [];

        public string? LeadHtml { get; init; }

        public Uri PageUrl(string title) =>
            new("https://ffxiv.consolegameswiki.com/wiki/" + title.Replace(' ', '_'));

        public Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WikiSearchHit>>(hits);

        public Task<string?> GetLeadSectionHtmlAsync(string pageTitle, CancellationToken ct) =>
            Task.FromResult<string?>(LeadHtml ?? $"<p>lead of {pageTitle}</p>");

        public Task<string?> GetPagePlainTextAsync(string pageTitle, CancellationToken ct) =>
            Task.FromResult<string?>($"plain text of {pageTitle}");

        public Task<IReadOnlyList<WikiSection>> GetSectionsAsync(string pageTitle, CancellationToken ct) =>
            Task.FromResult(Sections);

        public Task<string?> GetSectionHtmlAsync(string pageTitle, int sectionIndex, CancellationToken ct) =>
            Task.FromResult<string?>($"<p>section {sectionIndex} of {pageTitle}</p>");
    }
}
