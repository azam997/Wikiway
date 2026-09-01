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
        Assert.True(first.Score > result.Results[1].Score);
        Assert.Contains("consolegameswiki.com/wiki/First", first.PageUrl.ToString());
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
    public async Task NoMatchingSectionsLeavesThePlainPageResult()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(new WikiSearchHit("Iron Ingot", 1, ""))
        {
            Sections = [new WikiSection("1", "Lore", 1)],
        });

        var result = await provider.SearchAsync(Query("iron ingot", SearchCategory.Items), CancellationToken.None);

        var first = Assert.IsType<WikiPageResult>(Assert.Single(result.Results));
        Assert.Equal("Iron Ingot", first.Title);
    }

    [Fact]
    public async Task UnlocksCategoryBoostsExactTitleToFullScore()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("The Ultimate Weapon", 1, "")));

        var result = await provider.SearchAsync(
            Query("the ultimate weapon", SearchCategory.Unlocks), CancellationToken.None);

        Assert.Equal(1.0, result.Results[0].Score);
    }

    [Fact]
    public async Task UnlocksCategoryCombinesTitleBoostWithUnlockSections()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("The Gold Saucer", 1, ""))
        {
            Sections = [new WikiSection("3", "Unlocking", 1), new WikiSection("7", "Lore", 1)],
        });

        var result = await provider.SearchAsync(
            Query("the gold saucer", SearchCategory.Unlocks), CancellationToken.None);

        var sections = Assert.IsType<WikiSectionsResult>(result.Results[0]);
        var section = Assert.Single(sections.Sections);
        Assert.Equal("Unlocking", section.Heading);
        var page = Assert.IsType<WikiPageResult>(result.Results[1]);
        Assert.Equal(1.0, page.Score);
    }

    [Fact]
    public async Task RedirectSnippetDemotesTheHit()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("Momodi Modi", 1, "#REDIRECT [[Momodi]]")));

        var result = await provider.SearchAsync(Query("momodi"), CancellationToken.None);

        var hit = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.True(hit.Score <= 0.1);
    }

    [Fact]
    public async Task InfoboxSourceSnippetDemotesTheHit()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(
            new WikiSearchHit("Momodi", 1, "| name = Momodi\n| full-name = Momodi Modi")));

        var result = await provider.SearchAsync(Query("momodi"), CancellationToken.None);

        var hit = Assert.IsType<WikiPageResult>(result.Results[0]);
        Assert.True(hit.Score <= 0.1);
    }

    [Fact]
    public async Task SectionsBudgetExpiryKeepsThePageHits()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(new WikiSearchHit("Iron Ingot", 1, ""))
        {
            SectionsFailure = new OperationCanceledException(),
        });

        var result = await provider.SearchAsync(Query("iron ingot", SearchCategory.Items), CancellationToken.None);

        Assert.Equal(ProviderStatus.Ok, result.Status);
        var page = Assert.IsType<WikiPageResult>(Assert.Single(result.Results));
        Assert.Equal("Iron Ingot", page.Title);
    }

    [Fact]
    public void DisabledInConfigMeansUnavailable()
    {
        var provider = new ConsoleGamesWikiProvider(new StubWikiClient(), enabled: () => false);

        Assert.False(provider.IsAvailable);
    }

    private sealed class StubWikiClient(params WikiSearchHit[] hits) : IWikiApiClient
    {
        public IReadOnlyList<WikiSection> Sections { get; init; } = [];

        public Exception? SectionsFailure { get; init; }

        public Uri PageUrl(string title) =>
            new("https://ffxiv.consolegameswiki.com/wiki/" + title.Replace(' ', '_'));

        public Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WikiSearchHit>>(hits);

        public Task<IReadOnlyList<WikiSection>> GetSectionsAsync(string pageTitle, CancellationToken ct) =>
            SectionsFailure is { } failure
                ? Task.FromException<IReadOnlyList<WikiSection>>(failure)
                : Task.FromResult(Sections);

        public Task<string?> GetSectionHtmlAsync(string pageTitle, int sectionIndex, CancellationToken ct) =>
            Task.FromResult<string?>($"<p>section {sectionIndex} of {pageTitle}</p>");
    }
}
