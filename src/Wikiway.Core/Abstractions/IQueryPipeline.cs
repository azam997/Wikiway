using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

// The whole search: raw query text in, ranked results out. QueryOrchestrator is
// the only implementation; the plugin depends on this interface alone.
public interface IQueryPipeline
{
    Task<QueryResponse> ExecuteAsync(string rawQuery, CancellationToken ct);
}
