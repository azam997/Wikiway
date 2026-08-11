namespace Wikiway.Core.Abstractions;

public interface IWikiApiClient
{
    Uri PageUrl(string title);

    Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct);

    Task<string?> GetLeadSectionHtmlAsync(string pageTitle, CancellationToken ct);

    Task<string?> GetPagePlainTextAsync(string pageTitle, CancellationToken ct);
}

public sealed record WikiSearchHit(string Title, uint PageId, string SnippetHtml);
