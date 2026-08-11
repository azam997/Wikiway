namespace Wikiway.Core.Models;

public enum QueryIntent
{
    Unknown,
    Location,
    Unlock,
    Acquisition,
    General,
}

public sealed record NormalizedQuery(string Raw, string Term, QueryIntent Intent);
