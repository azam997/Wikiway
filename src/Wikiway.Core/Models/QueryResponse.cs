using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Models;

public sealed record QueryResponse(
    NormalizedQuery Query,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<ProviderResult> ProviderDetail,
    TimeSpan Elapsed = default);
