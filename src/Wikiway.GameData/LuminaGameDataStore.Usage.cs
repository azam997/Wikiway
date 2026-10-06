using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Item usage: the USAGE block (recipes that use the item, custom deliveries,
// collectable turn-ins, treasure maps, materia) and the reverse indexes behind
// it.
public sealed partial class LuminaGameDataStore
{
    // What-is-this-item-for reverse indexes behind the USAGE block, keyed by
    // Item row id.
    private sealed record UsageIndex(
        Dictionary<uint, int> RecipeUsesByItem,
        Dictionary<uint, List<uint>> DeliveryNpcsByItem,
        Dictionary<uint, List<CollectableTurnIn>> CollectableTurnInsByItem,
        Dictionary<uint, List<uint>> TreasureRanksByItem,
        Dictionary<uint, string> MateriaTagByItem);

    private UsageIndex BuildUsageIndex() => new(
        BuildRecipeUseLookup(),
        BuildDeliveryLookup(),
        BuildCollectableLookup(),
        BuildTreasureRankLookup(),
        BuildMateriaTagLookup());

    // Null when the item isn't used in anything the sheets track.
    private ItemUsage? BuildUsage(uint itemRowId)
    {
        var recipeUses = usageIndex.Value.RecipeUsesByItem.GetValueOrDefault(itemRowId);
        var deliveries = BuildDeliveries(itemRowId);
        var turnIns = usageIndex.Value.CollectableTurnInsByItem.GetValueOrDefault(itemRowId)
            ?? (IReadOnlyList<CollectableTurnIn>)[];
        var treasureMap = BuildTreasureMap(itemRowId);
        var materiaTag = usageIndex.Value.MateriaTagByItem.GetValueOrDefault(itemRowId, "");

        return recipeUses == 0 && deliveries.Count == 0 && turnIns.Count == 0 &&
               treasureMap == null && materiaTag.Length == 0
            ? null
            : new ItemUsage(recipeUses, deliveries, turnIns, treasureMap, materiaTag);
    }

    // Custom-delivery clients who want the item, with the quest that unlocks
    // each client.
    private List<DeliverySource> BuildDeliveries(uint itemRowId)
    {
        var deliveries = new List<DeliverySource>();
        if (!usageIndex.Value.DeliveryNpcsByItem.TryGetValue(itemRowId, out var npcIds))
            return deliveries;

        foreach (var npcId in npcIds.Distinct())
        {
            var row = Sheet<SatisfactionNpc>().GetRowOrDefault(npcId);
            if (row?.Npc.ValueNullable is not { } npc || npc.Singular.IsEmpty)
                continue;

            var quest = row.Value.QuestRequired.ValueNullable;
            deliveries.Add(new DeliverySource(
                TitleCase(npc.Singular.ExtractText()),
                quest is { Name.IsEmpty: false } q
                    ? new QuestLink(row.Value.QuestRequired.RowId, q.Name.ExtractText())
                    : null));
        }

        return deliveries;
    }

    // For timeworn maps: max party size and dig-site count per zone. Per-site
    // flags are left out on purpose, since a zone has several candidate sites.
    private TreasureMapInfo? BuildTreasureMap(uint itemRowId)
    {
        if (!usageIndex.Value.TreasureRanksByItem.TryGetValue(itemRowId, out var rankIds))
            return null;

        // Some map items sit on two rank rows (Timeworn Leather Map); the
        // stray row's spots carry Location 0 and contribute nothing.
        var partySize = 0;
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        var spots = SubrowSheet<TreasureSpot>();
        foreach (var rankId in rankIds)
        {
            var rank = Sheet<TreasureHuntRank>().GetRowOrDefault(rankId);
            if (rank == null || !spots.HasRow(rankId))
                continue;

            var any = false;
            foreach (var spot in spots.GetRow(rankId))
            {
                var zone = spot.Location.ValueNullable?.Territory.ValueNullable?
                    .PlaceName.ValueNullable?.Name.ExtractText() ?? "";
                if (zone.Length == 0)
                    continue;

                if (!counts.ContainsKey(zone))
                    order.Add(zone);
                counts[zone] = counts.GetValueOrDefault(zone) + 1;
                any = true;
            }

            if (any)
                partySize = Math.Max(partySize, rank.Value.MaxPartySize);
        }

        return order.Count == 0
            ? null
            : new TreasureMapInfo(partySize, order.Select(z => new TreasureZone(z, counts[z])).ToList());
    }

    // Ingredient -> number of recipes using it (counted once per recipe).
    private Dictionary<uint, int> BuildRecipeUseLookup()
    {
        var lookup = new Dictionary<uint, int>();
        var counted = new HashSet<uint>();
        foreach (var recipe in Sheet<Recipe>())
        {
            if (recipe.ItemResult.RowId == 0)
                continue;

            counted.Clear();
            foreach (var ingredient in recipe.Ingredient)
            {
                if (ingredient.RowId != 0 && counted.Add(ingredient.RowId))
                    lookup[ingredient.RowId] = lookup.GetValueOrDefault(ingredient.RowId) + 1;
            }
        }

        return lookup;
    }

    // Item -> SatisfactionNpc rows (custom-delivery clients) asking for it at
    // any rank.
    private Dictionary<uint, List<uint>> BuildDeliveryLookup()
    {
        var itemsBySupply = new Dictionary<uint, List<uint>>();
        foreach (var group in SubrowSheet<SatisfactionSupply>())
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId != 0)
                    Add(itemsBySupply, group.RowId, sub.Item.RowId);
            }
        }

        // Each rank's SupplyIndex names a SatisfactionSupply parent row; the
        // same item can repeat across ranks, deduped at read time.
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var npc in Sheet<SatisfactionNpc>())
        {
            if (npc.Npc.RowId == 0)
                continue;

            foreach (var param in npc.SatisfactionNpcParams)
            {
                if (param.SupplyIndex <= 0 || !itemsBySupply.TryGetValue((uint)param.SupplyIndex, out var itemIds))
                    continue;

                foreach (var itemId in itemIds)
                    Add(lookup, itemId, npc.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<CollectableTurnIn>> BuildCollectableLookup()
    {
        // Several collectable shops list the same turn-in; identical
        // (level band, payout) entries collapse to one.
        var lookup = new Dictionary<uint, List<CollectableTurnIn>>();
        var seen = new HashSet<(uint Item, CollectableTurnIn TurnIn)>();
        foreach (var group in SubrowSheet<CollectablesShopItem>())
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId == 0)
                    continue;

                var turnIn = new CollectableTurnIn(
                    sub.LevelMin,
                    sub.LevelMax,
                    sub.CollectablesShopRewardScrip.ValueNullable?.HighReward ?? 0);
                if (!seen.Add((sub.Item.RowId, turnIn)))
                    continue;

                if (!lookup.TryGetValue(sub.Item.RowId, out var list))
                    lookup[sub.Item.RowId] = list = [];
                list.Add(turnIn);
            }
        }

        return lookup;
    }

    // Map item -> TreasureHuntRank rows; TreasureSpot subrows hang off the
    // rank.
    private Dictionary<uint, List<uint>> BuildTreasureRankLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var rank in Sheet<TreasureHuntRank>())
        {
            if (rank.ItemName.RowId != 0)
                Add(lookup, rank.ItemName.RowId, rank.RowId);
        }

        return lookup;
    }

    // Materia item -> "Critical Hit +12" style tag.
    private Dictionary<uint, string> BuildMateriaTagLookup()
    {
        // Zero-value grades exist (retired main-stat materia); they get no tag.
        var lookup = new Dictionary<uint, string>();
        foreach (var materia in Sheet<Materia>())
        {
            var param = materia.BaseParam.ValueNullable?.Name.ExtractText() ?? "";
            if (param.Length == 0)
                continue;

            for (var i = 0; i < materia.Item.Count && i < materia.Value.Count; i++)
            {
                if (materia.Item[i].RowId != 0 && materia.Value[i] != 0)
                    lookup.TryAdd(materia.Item[i].RowId, $"{param} +{materia.Value[i]}");
            }
        }

        return lookup;
    }
}
