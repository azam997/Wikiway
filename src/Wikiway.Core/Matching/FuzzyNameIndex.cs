using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Matching;

public sealed record NameMatch(NameIndexEntry Entry, double Score);

public sealed class FuzzyNameIndex
{
    private readonly List<NameIndexEntry> entries;
    private readonly ILookup<string, NameIndexEntry> byName;

    public FuzzyNameIndex(IEnumerable<NameIndexEntry> source)
    {
        entries = source.Where(e => e.Name.Length > 0).ToList();
        byName = entries.ToLookup(e => e.Name);
    }

    public int Count => entries.Count;

    public IReadOnlyList<NameMatch> Search(string term, int limit = 8)
    {
        if (term.Length == 0)
            return [];

        var matches = new List<NameMatch>();
        var seen = new HashSet<(EntityKind, uint)>();

        foreach (var entry in byName[term])
            Add(matches, seen, entry, 1.0);

        if (matches.Count < limit)
        {
            foreach (var entry in entries)
            {
                if (entry.Name.Length > term.Length && entry.Name.StartsWith(term, StringComparison.Ordinal))
                    Add(matches, seen, entry, 0.85);
            }
        }

        if (matches.Count < limit)
        {
            foreach (var entry in entries)
            {
                if (entry.Name.Length > term.Length && entry.Name.Contains(term, StringComparison.Ordinal))
                    Add(matches, seen, entry, 0.7);
            }
        }

        // Typo tier - only worth the scan when nothing better matched.
        if (matches.Count == 0)
        {
            foreach (var entry in entries)
            {
                if (Math.Abs(entry.Name.Length - term.Length) > 2)
                    continue;

                var distance = BoundedLevenshtein(term, entry.Name, 2);
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

    private static void Add(List<NameMatch> matches, HashSet<(EntityKind, uint)> seen,
        NameIndexEntry entry, double score)
    {
        if (seen.Add((entry.Kind, entry.RowId)))
            matches.Add(new NameMatch(entry, score));
    }

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
