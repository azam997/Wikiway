using System.Text.Json;
using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Wiki;

public sealed class ConsoleGamesWikiClient : IWikiApiClient
{
    public const string Host = "ffxiv.consolegameswiki.com";
    private const string ApiBase = $"https://{Host}/mediawiki/api.php";
    private const string UserAgent = "Wikiway/0.1 (+https://github.com/azam997/Wikiway)";

    private readonly HttpClient http;

    public ConsoleGamesWikiClient(HttpClient http)
    {
        this.http = http;
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    public Uri PageUrl(string title) =>
        new($"https://{Host}/wiki/{Uri.EscapeDataString(title.Replace(' ', '_'))}");

    public async Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct)
    {
        var url = $"{ApiBase}?action=query&list=search&srsearch={Uri.EscapeDataString(term)}&srlimit={limit}&format=json";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("query", out var query) ||
            !query.TryGetProperty("search", out var search))
            return [];

        var hits = new List<WikiSearchHit>();
        foreach (var entry in search.EnumerateArray())
        {
            var title = entry.GetProperty("title").GetString();
            var pageId = entry.GetProperty("pageid").GetUInt32();
            var snippet = entry.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";
            if (!string.IsNullOrEmpty(title))
                hits.Add(new WikiSearchHit(title, pageId, snippet));
        }

        return hits;
    }

    public async Task<string?> GetLeadSectionHtmlAsync(string pageTitle, CancellationToken ct)
    {
        var url = $"{ApiBase}?action=parse&page={Uri.EscapeDataString(pageTitle)}&prop=text&section=0&redirects=1&format=json";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("parse", out var parse) ||
            !parse.TryGetProperty("text", out var text))
            return null;

        // parse.text is { "*": "<html>" } in the default (non-formatversion=2) output.
        return text.TryGetProperty("*", out var html) ? html.GetString() : text.GetString();
    }

    public async Task<string?> GetPagePlainTextAsync(string pageTitle, CancellationToken ct)
    {
        var url = $"{ApiBase}?action=parse&page={Uri.EscapeDataString(pageTitle)}&prop=text&redirects=1&format=json";
        using var doc = await GetJsonAsync(url, ct).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("parse", out var parse) ||
            !parse.TryGetProperty("text", out var text))
            return null;

        var html = text.TryGetProperty("*", out var star) ? star.GetString() : text.GetString();
        return html == null ? null : HtmlText.Strip(html);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }
}
