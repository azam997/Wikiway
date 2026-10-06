using System.Text;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public sealed class QueryNormalizer
{
    // Longest phrases first so "how do i unlock" wins over "how do i".
    private static readonly (string Phrase, QueryIntent Intent)[] LeadingPhrases =
    [
        ("where can i find", QueryIntent.Location),
        ("where do i find", QueryIntent.Location),
        ("where is", QueryIntent.Location),
        ("where are", QueryIntent.Location),
        ("where's", QueryIntent.Location),
        ("location of", QueryIntent.Location),

        ("how do i unlock", QueryIntent.Unlock),
        ("how to unlock", QueryIntent.Unlock),
        ("what do i need for", QueryIntent.Unlock),
        ("what do i need to unlock", QueryIntent.Unlock),
        ("requirements for", QueryIntent.Unlock),
        ("unlock", QueryIntent.Unlock),

        ("where do i get", QueryIntent.Acquisition),
        ("where can i get", QueryIntent.Acquisition),
        ("how do i get", QueryIntent.Acquisition),
        ("how do i obtain", QueryIntent.Acquisition),
        ("how do i farm", QueryIntent.Acquisition),
        ("how to get", QueryIntent.Acquisition),
        ("how to obtain", QueryIntent.Acquisition),
        ("how to farm", QueryIntent.Acquisition),
        ("source of", QueryIntent.Acquisition),

        ("tell me about", QueryIntent.General),
        ("what is", QueryIntent.General),
        ("what are", QueryIntent.General),
        ("who is", QueryIntent.General),
        ("who's", QueryIntent.General),
    ];

    // The first prefix listed for a category is the one Prefixed writes.
    private static readonly (string Prefix, SearchCategory Category)[] Prefixes =
    [
        ("item", SearchCategory.Items),
        ("quest", SearchCategory.Unlocks),
        ("npc", SearchCategory.Npcs),
        ("gather", SearchCategory.Gathering),
        ("gathering", SearchCategory.Gathering),
        ("unlock", SearchCategory.Unlocks),
        ("unlockable", SearchCategory.Unlocks),
        ("area", SearchCategory.Unlocks),
        // Kinds without a filter of their own: the prefix is just dropped.
        ("duty", SearchCategory.All),
        ("orchestrion", SearchCategory.All),
        ("vista", SearchCategory.All),
        ("hunt", SearchCategory.All),
        ("mark", SearchCategory.All),
        ("fate", SearchCategory.All),
        ("leve", SearchCategory.All),
    ];

    // Lookups opened from a result card or a right-click carry their filter
    // as visible text, so the search box always shows what is being searched.
    public static string Prefixed(SearchCategory category, string term)
    {
        if (category == SearchCategory.All)
            return term;

        var prefix = Prefixes.First(p => p.Category == category).Prefix;
        return $"{prefix}: {term}";
    }

    public NormalizedQuery Normalize(string raw)
    {
        var (unprefixed, category) = SplitPrefix(raw);
        var text = StripPunctuation(unprefixed).Trim();
        var lowered = text.ToLowerInvariant();

        var intent = QueryIntent.Unknown;
        foreach (var (phrase, phraseIntent) in LeadingPhrases)
        {
            if (lowered.StartsWith(phrase, StringComparison.Ordinal) &&
                (lowered.Length == phrase.Length || lowered[phrase.Length] == ' '))
            {
                intent = phraseIntent;
                lowered = lowered[phrase.Length..].Trim();
                break;
            }
        }

        lowered = StripLeadingArticle(lowered);

        // A typed leading phrase is a stronger signal than the prefix.
        if (intent == QueryIntent.Unknown)
        {
            intent = category switch
            {
                SearchCategory.Items => QueryIntent.Acquisition,
                SearchCategory.Npcs => QueryIntent.Location,
                SearchCategory.Gathering => QueryIntent.Acquisition,
                SearchCategory.Unlocks => QueryIntent.Unlock,
                _ => QueryIntent.Unknown,
            };
        }

        return new NormalizedQuery(raw, lowered, intent, category);
    }

    private static (string Text, SearchCategory Category) SplitPrefix(string raw)
    {
        var colon = raw.IndexOf(':');
        if (colon > 0)
        {
            var typed = raw[..colon].Trim();
            foreach (var (prefix, category) in Prefixes)
            {
                if (typed.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                    return (raw[(colon + 1)..], category);
            }
        }

        return (raw, SearchCategory.All);
    }

    private static readonly string[] Articles = ["the ", "an ", "a "];

    // "how do i get an iron ingot" leaves "an iron ingot" after phrase stripping;
    // a leading article only hurts matching, and names that begin with one
    // ("The Navel") stay findable via the bare aliases in FuzzyNameIndex.
    public static string StripLeadingArticle(string text)
    {
        foreach (var article in Articles)
        {
            if (text.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return text[article.Length..].TrimStart();
        }

        return text;
    }

    private static string StripPunctuation(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '?' or '!' or '.' or ',' or '"')
                continue;
            sb.Append(c == '’' ? '\'' : c);
        }

        // Collapse runs of whitespace left behind by stripping.
        var parts = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
