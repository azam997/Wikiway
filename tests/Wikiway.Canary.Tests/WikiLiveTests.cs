using System.Diagnostics;
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
    public async Task LeadSectionOfKnownPageIsNonEmptyHtml()
    {
        var html = await Live(() => client.GetLeadSectionHtmlAsync("Aether Currents", CancellationToken.None));

        Assert.NotNull(html);
        Assert.Contains("<", html);
        Assert.True(html.Length > 100, "lead section suspiciously short");
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
