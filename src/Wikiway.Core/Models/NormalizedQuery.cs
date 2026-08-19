namespace Wikiway.Core.Models;

public enum QueryIntent
{
    Unknown,
    Location,
    Unlock,
    Acquisition,
    General,
}

public enum SearchCategory
{
    Other,
    Items,
    Quests,
    Duties,
    Npcs,
    Areas,
}

public sealed record NormalizedQuery(
    string Raw, string Term, QueryIntent Intent, SearchCategory Category = SearchCategory.Other);
