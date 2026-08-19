using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

public interface IQueryPipeline
{
    Task<QueryResponse> ExecuteAsync(string rawQuery, SearchCategory category, CancellationToken ct);
}
