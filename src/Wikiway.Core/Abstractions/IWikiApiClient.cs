namespace Wikiway.Core.Abstractions;

public interface IWikiApiClient
{
    Uri PageUrl(string title);

    Task<IReadOnlyList<WikiSearchHit>> SearchAsync(string term, int limit, CancellationToken ct);

    Task<string?> GetPagePlainTextAsync(string pageTitle, CancellationToken ct);

    Task<IReadOnlyList<WikiSection>> GetSectionsAsync(string pageTitle, CancellationToken ct);

    Task<string?> GetSectionHtmlAsync(string pageTitle, int sectionIndex, CancellationToken ct);
}

public sealed record WikiSearchHit(string Title, uint PageId, string SnippetHtml);

// Index stays a string: MediaWiki emits "T-1" for transcluded sections.
public sealed record WikiSection(string Index, string Title, int Level);
