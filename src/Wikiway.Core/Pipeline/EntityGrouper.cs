using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class EntityGrouper
{
    // Same-named ENpc copies with at most one event handler are quest-scene
    // spawns, not homes (probed: the real Momodi carries 31 handlers, her scene
    // copies exactly one). Once any copy in a group reaches the threshold, the
    // sub-threshold copies' placements are hidden to avoid cutscene spoilers.
    private const int PrimaryHandlerThreshold = 2;

    // Story NPCs leave a placement per MSQ stage and no handler pattern marks
    // the current one (probed: Y'shtola has 136 multi-handler placements across
    // 56 zones, Raubahn 16; spawn conditions live in compiled quest scripts).
    // When a quest-involved group still holds this many pins after filtering,
    // none of them is trustworthy - show none and let the wiki answer instead.
    private const int TrustedLocationCap = 4;

    public static IReadOnlyList<SearchResult> Collapse(
        IReadOnlyList<SearchResult> results, Func<uint, bool>? sceneQuestVisible = null)
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
                output[group.Index] = Merge(group, sceneQuestVisible);
        }

        return output;
    }

    private static EntityCardResult Merge(Group group, Func<uint, bool>? sceneQuestVisible)
    {
        var hasPrimary = group.Members.Any(m =>
            m.Entity is NpcEntity { EventHandlers: >= PrimaryHandlerThreshold });

        var locations = new List<MapLocation>();
        var seen = new HashSet<(uint MapId, float X, float Y)>();
        var hidden = 0;
        var appearances = new List<CutsceneAppearance>();
        var seenScenes = new HashSet<(uint QuestId, uint MapId, float X, float Y)>();

        void Hide(NpcEntity npc)
        {
            hidden++;

            // A lone quest handler is the scene-copy signature; hidden copies
            // carrying several are interaction variants, not appearances.
            if (npc.SceneQuests is [var scene] &&
                (sceneQuestVisible == null || sceneQuestVisible(scene.Quest.RowId)) &&
                seenScenes.Add((scene.Quest.RowId,
                    npc.Location?.MapId ?? 0,
                    MathF.Round(npc.Location?.MapX ?? 0, 1),
                    MathF.Round(npc.Location?.MapY ?? 0, 1))))
            {
                appearances.Add(scene with { Location = npc.Location });
            }
        }

        var shown = new List<NpcEntity>();
        foreach (var member in group.Members)
        {
            if (member.Entity is not NpcEntity npc)
                continue;

            if (npc.Location is not { } location ||
                (hasPrimary && npc.EventHandlers < PrimaryHandlerThreshold))
            {
                Hide(npc);
                continue;
            }

            shown.Add(npc);

            // Coordinates render at one decimal, so 11.68 and 11.71 are the same spot.
            if (seen.Add((location.MapId, MathF.Round(location.MapX, 1), MathF.Round(location.MapY, 1))))
                locations.Add(location);
        }

        if (locations.Count > TrustedLocationCap &&
            group.Members.Any(m => m.Entity is NpcEntity { SceneQuests.Count: > 0 }))
        {
            locations.Clear();
            foreach (var npc in shown)
                Hide(npc);
        }

        appearances.Sort((a, b) => a.ExpansionOrder != b.ExpansionOrder
            ? a.ExpansionOrder.CompareTo(b.ExpansionOrder)
            : string.Compare(a.Quest.Name, b.Quest.Name, StringComparison.OrdinalIgnoreCase));

        return group.Representative with
        {
            MergedLocations = locations,
            MergedCount = group.Members.Count,
            MergedHidden = hidden,
            CutsceneAppearances = appearances,
        };
    }

    private sealed class Group(EntityCardResult representative, int index)
    {
        public EntityCardResult Representative { get; } = representative;
        public int Index { get; } = index;
        public List<EntityCardResult> Members { get; } = [];
    }
}
