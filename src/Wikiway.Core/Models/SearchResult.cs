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

    // Set when duplicate same-named entities were collapsed into this card.
    public IReadOnlyList<MapLocation> MergedLocations { get; init; } = [];

    public int MergedCount { get; init; } = 1;

    public int MergedHidden { get; init; }

    // Set when a same-titled wiki sections result was absorbed into this card.
    public IReadOnlyList<WikiSectionText> WikiSections { get; init; } = [];

    public Uri? WikiUrl { get; init; }
}

public sealed record WikiPageResult : SearchResult
{
    public required Uri PageUrl { get; init; }

    public string? Snippet { get; init; }

    public string? Lead { get; init; }
}

public sealed record WikiSectionText(string Heading, string Text);

public sealed record WikiSectionsResult : SearchResult
{
    public required Uri PageUrl { get; init; }

    public required IReadOnlyList<WikiSectionText> Sections { get; init; }
}
