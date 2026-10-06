using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Models;

// Everything the result list draws: ranked results plus each provider's status
// (for the count pills and failure footer).
public sealed record QueryResponse(
    NormalizedQuery Query,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<ProviderResult> ProviderDetail,
    TimeSpan Elapsed = default);
