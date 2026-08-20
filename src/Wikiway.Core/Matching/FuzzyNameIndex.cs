using Wikiway.Core.Abstractions;
using Wikiway.Core.Pipeline;

namespace Wikiway.Core.Matching;

public sealed record NameMatch(NameIndexEntry Entry, double Score);

public sealed class FuzzyNameIndex
{
    private readonly record struct IndexedName(NameIndexEntry Entry, string Bare);

    private readonly List<IndexedName> entries;
    private readonly ILookup<string, NameIndexEntry> byName;

    public FuzzyNameIndex(IEnumerable<NameIndexEntry> source)
    {
        // Queries arrive with their leading article stripped, so names that start
        // with one ("the navel") are also keyed by their bare form.
        entries = source
            .Where(e => e.Name.Length > 0)
            .Select(e => new IndexedName(e, QueryNormalizer.StripLeadingArticle(e.Name)))
            .ToList();
        byName = entries.SelectMany(Keys).ToLookup(p => p.Key, p => p.Entry);
    }

    private static IEnumerable<(string Key, NameIndexEntry Entry)> Keys(IndexedName name)
    {
        yield return (name.Entry.Name, name.Entry);
        if (name.Bare != name.Entry.Name)
            yield return (name.Bare, name.Entry);
    }

    public int Count => entries.Count;

    public IReadOnlyList<NameMatch> Search(string term, int limit = 8, IReadOnlyList<EntityKind>? kinds = null)
    {
        if (term.Length == 0)
            return [];

        var matches = new List<NameMatch>();
        var seen = new HashSet<(EntityKind, uint)>();

        foreach (var entry in byName[term])
        {
            if (Matches(kinds, entry.Kind))
                Add(matches, seen, entry, 1.0);
        }

        if (matches.Count < limit)
        {
            foreach (var (entry, bare) in entries)
            {
                if (!Matches(kinds, entry.Kind))
                    continue;
                if (IsPrefix(term, entry.Name) || IsPrefix(term, bare))
                    Add(matches, seen, entry, 0.85);
            }
        }

        if (matches.Count < limit)
        {
            foreach (var (entry, _) in entries)
            {
                if (!Matches(kinds, entry.Kind))
                    continue;
                if (entry.Name.Length > term.Length && entry.Name.Contains(term, StringComparison.Ordinal))
                    Add(matches, seen, entry, 0.7);
            }
        }

        // Typo tier - only worth the scan when nothing better matched.
        if (matches.Count == 0)
        {
            foreach (var (entry, bare) in entries)
            {
                if (!Matches(kinds, entry.Kind))
                    continue;

                var distance = BoundedDistance(term, entry.Name);
                if (bare != entry.Name)
                {
                    var bareDistance = BoundedDistance(term, bare);
                    if (distance < 0 || (bareDistance >= 0 && bareDistance < distance))
                        distance = bareDistance;
                }

                if (distance >= 0)
                    Add(matches, seen, entry, 0.6 - (0.05 * distance));
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Entry.Name.Length)
            .Take(limit)
            .ToList();
    }

    private static bool Matches(IReadOnlyList<EntityKind>? kinds, EntityKind kind) =>
        kinds == null || kinds.Contains(kind);

    private static void Add(List<NameMatch> matches, HashSet<(EntityKind, uint)> seen,
        NameIndexEntry entry, double score)
    {
        if (seen.Add((entry.Kind, entry.RowId)))
            matches.Add(new NameMatch(entry, score));
    }

    private static bool IsPrefix(string term, string name) =>
        name.Length > term.Length && name.StartsWith(term, StringComparison.Ordinal);

    private static int BoundedDistance(string term, string name) =>
        Math.Abs(name.Length - term.Length) > 2 ? -1 : BoundedLevenshtein(term, name, 2);

    // Returns edit distance if <= max, otherwise -1. Two rows of the usual DP table.
    private static int BoundedLevenshtein(string a, string b, int max)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];

            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > max)
                return -1;

            (previous, current) = (current, previous);
        }

        return previous[b.Length] <= max ? previous[b.Length] : -1;
    }
}
