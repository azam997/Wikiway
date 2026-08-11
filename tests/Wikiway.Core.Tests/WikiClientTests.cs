using System.Net;
using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Core.Tests;

public class WikiClientTests
{
    // Shape captured from a live query response.
    private const string SearchJson = """
        {
          "batchcomplete": "",
          "query": {
            "searchinfo": { "totalhits": 42 },
            "search": [
              {
                "ns": 0,
                "title": "Aether Currents",
                "pageid": 12345,
                "size": 2000,
                "wordcount": 300,
                "snippet": "Attune to all <span class=\"searchmatch\">aether currents</span> in a zone",
                "timestamp": "2026-01-01T00:00:00Z"
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task SearchParsesTitlePageIdAndSnippet()
    {
        var handler = new CannedHandler(SearchJson);
        var client = new ConsoleGamesWikiClient(new HttpClient(handler));

        var hits = await client.SearchAsync("aether currents", 5, CancellationToken.None);

        var hit = Assert.Single(hits);
        Assert.Equal("Aether Currents", hit.Title);
        Assert.Equal(12345u, hit.PageId);
        Assert.Contains("searchmatch", hit.SnippetHtml);
    }

    [Fact]
    public async Task SearchSendsDescriptiveUserAgent()
    {
        var handler = new CannedHandler(SearchJson);
        var client = new ConsoleGamesWikiClient(new HttpClient(handler));

        await client.SearchAsync("test", 5, CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        var userAgent = handler.LastRequest!.Headers.UserAgent.ToString();
        Assert.Contains("Wikiway", userAgent);
        Assert.Contains("github.com", userAgent);
    }

    [Fact]
    public async Task MissingSearchArrayMeansEmptyNotThrow()
    {
        var handler = new CannedHandler("""{"batchcomplete": ""}""");
        var client = new ConsoleGamesWikiClient(new HttpClient(handler));

        var hits = await client.SearchAsync("anything", 5, CancellationToken.None);

        Assert.Empty(hits);
    }

    [Fact]
    public async Task LeadSectionUnwrapsTheStarProperty()
    {
        var handler = new CannedHandler("""{"parse": {"title": "X", "text": {"*": "<p>lead</p>"}}}""");
        var client = new ConsoleGamesWikiClient(new HttpClient(handler));

        var html = await client.GetLeadSectionHtmlAsync("X", CancellationToken.None);

        Assert.Equal("<p>lead</p>", html);
    }

    [Fact]
    public void PageUrlUsesUnderscores()
    {
        var client = new ConsoleGamesWikiClient(new HttpClient(new CannedHandler("{}")));

        var url = client.PageUrl("Aether Currents");

        Assert.Equal("https://ffxiv.consolegameswiki.com/wiki/Aether_Currents", url.ToString());
    }

    private sealed class CannedHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
