using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Models;

public sealed record QueryResponse(
    NormalizedQuery Query,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<ProviderResult> ProviderDetail,
    SynthesizedAnswer? Answer,
    TimeSpan Elapsed = default);

public sealed record SynthesizedAnswer(string Text, IReadOnlyList<Citation> Citations);
