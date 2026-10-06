namespace Wikiway.Core.Models;

// What the question asks for, read from leading phrases ("where is", "how do I
// get", "how to unlock"). It nudges ranking and picks which wiki sections to
// fetch.
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
