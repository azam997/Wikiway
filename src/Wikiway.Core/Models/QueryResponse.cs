using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Models;

public sealed record QueryResponse(
    NormalizedQuery Query,
    IReadOnlyList<SearchResult> Results,
    IReadOnlyList<ProviderResult> ProviderDetail,
    SynthesizedAnswer? Answer);

public sealed record SynthesizedAnswer(string Text, IReadOnlyList<Citation> Citations);
