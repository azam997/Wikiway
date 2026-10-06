namespace Wikiway.Core.Models;

public enum QueryIntent
{
    Unknown,
    Location,
    Unlock,
    Acquisition,
    General,
}

// Chosen by an optional prefix on the query text ("item: iron ingot");
// All means no prefix, so every kind of result is searched.
public enum SearchCategory
{
    All,
    Items,
    Npcs,
    Gathering,
    Unlocks,
}

public sealed record NormalizedQuery(
    string Raw, string Term, QueryIntent Intent, SearchCategory Category = SearchCategory.All);
