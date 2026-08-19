using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class EntityGrouper
{
    // Same-named ENpc copies with at most one event handler are quest-scene
    // spawns, not homes (probed: the real Momodi carries 31 handlers, her scene
    // copies exactly one). Once any copy in a group reaches the threshold, the
    // sub-threshold copies' placements are hidden to avoid cutscene spoilers.
    private const int PrimaryHandlerThreshold = 2;

    public static IReadOnlyList<SearchResult> Collapse(IReadOnlyList<SearchResult> results)
    {
        var groups = new Dictionary<(Type Kind, string Name), Group>();
        var output = new List<SearchResult>(results.Count);

        foreach (var result in results)
        {
            if (result is not EntityCardResult card)
            {
                output.Add(result);
                continue;
            }

            var key = (card.Entity.GetType(), card.Entity.Name.ToLowerInvariant());
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group(card, output.Count);
                groups.Add(key, group);
                output.Add(card);
            }

            group.Members.Add(card);
        }

        foreach (var group in groups.Values)
        {
            if (group.Members.Count > 1)
                output[group.Index] = Merge(group);
        }

        return output;
    }

    private static EntityCardResult Merge(Group group)
    {
        var hasPrimary = group.Members.Any(m =>
            m.Entity is NpcEntity { EventHandlers: >= PrimaryHandlerThreshold });

        var locations = new List<MapLocation>();
        var seen = new HashSet<(uint MapId, float X, float Y)>();
        var hidden = 0;

        foreach (var member in group.Members)
        {
            if (member.Entity is not NpcEntity npc)
                continue;

            if (npc.Location is not { } location ||
                (hasPrimary && npc.EventHandlers < PrimaryHandlerThreshold))
            {
                hidden++;
                continue;
            }

            // Coordinates render at one decimal, so 11.68 and 11.71 are the same spot.
            if (seen.Add((location.MapId, MathF.Round(location.MapX, 1), MathF.Round(location.MapY, 1))))
                locations.Add(location);
        }

        return group.Representative with
        {
            MergedLocations = locations,
            MergedCount = group.Members.Count,
            MergedHidden = hidden,
        };
    }

    private sealed class Group(EntityCardResult representative, int index)
    {
        public EntityCardResult Representative { get; } = representative;
        public int Index { get; } = index;
        public List<EntityCardResult> Members { get; } = [];
    }
}
