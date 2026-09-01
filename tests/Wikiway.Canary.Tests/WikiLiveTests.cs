using System.Diagnostics;
using Wikiway.Core.Models;
using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Canary.Tests;

// Live contract tests against the real wiki. The rule: can't reach the network
// at all -> skip; got an HTTP error or an unexpected shape -> FAIL, because
// that's exactly the breakage these exist to catch.
[Trait("Category", "WikiLive")]
public class WikiLiveTests : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly ConsoleGamesWikiClient client;

    public WikiLiveTests()
    {
        client = new ConsoleGamesWikiClient(http);
    }

    public void Dispose() => http.Dispose();

    [Fact]
    public async Task SearchReturnsHitsWithExpectedFields()
    {
        var hits = await Live(() => client.SearchAsync("aether currents", 5, CancellationToken.None));

        Assert.NotEmpty(hits);
        Assert.All(hits, h =>
        {
            Assert.False(string.IsNullOrEmpty(h.Title));
            Assert.True(h.PageId > 0);
        });
        Assert.Contains(hits, h => h.SnippetHtml.Length > 0);
    }

    [Fact]
    public async Task GibberishSearchIsEmptyNotAnError()
    {
        var hits = await Live(() => client.SearchAsync("xqzzqxplzzt", 5, CancellationToken.None));

        Assert.Empty(hits);
    }

    [Fact]
    public async Task SearchAnswersWithinTenSeconds()
    {
        var watch = Stopwatch.StartNew();
        await Live(() => client.SearchAsync("momodi", 3, CancellationToken.None));
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    // The drift alarm for the wiki's heading conventions: if item pages stop
    // using headings our extractor recognizes, this fails loudly.
    [Fact]
    public async Task ItemPageSectionsSatisfyTheExtractor()
    {
        var sections = await Live(() => client.GetSectionsAsync("Iron Ingot", CancellationToken.None));

        var picked = SectionExtractor.SelectSections(SearchCategory.Items, sections);
        Assert.NotEmpty(picked);

        var html = await Live(() =>
            client.GetSectionHtmlAsync("Iron Ingot", int.Parse(picked[0].Index), CancellationToken.None));
        Assert.NotNull(html);
        Assert.True(HtmlText.Strip(html).Length > 0, "section body stripped to nothing");
    }

    // Duty drops exist nowhere in the client sheets (probed 2026-09-01), so
    // the wiki's "Duties" subsection is the only place this answer can come
    // from - if the heading or its body drifts, the drop line silently dies.
    [Fact]
    public async Task DropOnlyItemPageYieldsItsDutiesSection()
    {
        var sections = await Live(() => client.GetSectionsAsync("Aithon Whistle", CancellationToken.None));

        var picked = SectionExtractor.SelectSections(SearchCategory.Items, sections);
        var duties = picked.FirstOrDefault(s => s.Title.Contains("dut", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(duties);

        var html = await Live(() =>
            client.GetSectionHtmlAsync("Aithon Whistle", int.Parse(duties.Index), CancellationToken.None));
        Assert.NotNull(html);
        Assert.Contains("Bowl of Embers", HtmlText.Strip(html), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> Live<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (HttpRequestException e) when (e.StatusCode == null)
        {
            Assert.Skip($"network unavailable: {e.Message}");
            return default!;
        }
        catch (TaskCanceledException)
        {
            Assert.Skip("network timeout");
            return default!;
        }
    }
}
