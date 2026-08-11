using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

public interface IDocumentRetriever
{
    Task<RetrievedDocument?> RetrieveAsync(SearchResult hit, CancellationToken ct);
}

public sealed record RetrievedDocument(string Title, Uri SourceUrl, string PlainText);
