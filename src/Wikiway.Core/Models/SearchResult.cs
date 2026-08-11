namespace Wikiway.Core.Models;

public sealed record Citation(string Label, Uri? Url = null);

public abstract record SearchResult
{
    public required string Title { get; init; }

    public required Citation Source { get; init; }

    // Provider-local relevance, 0..1. The ranker weighs providers against each other.
    public double Score { get; init; }
}

public sealed record EntityCardResult : SearchResult
{
    public required GameEntity Entity { get; init; }
}

public sealed record WikiPageResult : SearchResult
{
    public required Uri PageUrl { get; init; }

    public string? Snippet { get; init; }

    public string? LeadSectionHtml { get; init; }
}
