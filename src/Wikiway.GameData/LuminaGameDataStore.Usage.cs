using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Item usage: the USAGE block (recipes that use the item, custom deliveries,
// collectable turn-ins, treasure maps). Built with ACQUISITION when the card
// is opened (GetItemDetails); each part is one scan of its sheet for rows
// naming the item. The materia stat is read per result instead (.Items.cs),
// because the collapsed row shows it.
public sealed partial class LuminaGameDataStore
{
    // Null when the item isn't used in anything the sheets track.
    private ItemUsage? BuildUsage(uint itemRowId)
    {
        var recipeUses = CountRecipeUses(itemRowId);
        var deliveries = BuildDeliveries(itemRowId);
        var turnIns = BuildCollectableTurnIns(itemRowId);
        var treasureMap = BuildTreasureMap(itemRowId);

        return recipeUses == 0 && deliveries.Count == 0 && turnIns.Count == 0 && treasureMap == null
            ? null
            : new ItemUsage(recipeUses, deliveries, turnIns, treasureMap);
    }

    // Recipes taking the item as an ingredient, each counted once.
    private int CountRecipeUses(uint itemRowId)
    {
        var count = 0;
        foreach (var recipe in Sheet<Recipe>())
        {
            if (recipe.ItemResult.RowId != 0 && recipe.Ingredient.Any(ingredient => ingredient.RowId == itemRowId))
                count++;
        }

        return count;
    }

    // Custom-delivery clients who want the item, with the quest that unlocks
    // each client.
    private List<DeliverySource> BuildDeliveries(uint itemRowId)
    {
        var deliveries = new List<DeliverySource>();
        var supplyGroups = new HashSet<uint>();
        foreach (var group in SubrowSheet<SatisfactionSupply>())
        {
            if (group.Any(sub => sub.Item.RowId == itemRowId))
                supplyGroups.Add(group.RowId);
        }

        if (supplyGroups.Count == 0)
            return deliveries;

        // Each client rank's SupplyIndex names a SatisfactionSupply parent row.
        // The same item can repeat across ranks; the client is listed once.
        foreach (var row in Sheet<SatisfactionNpc>())
        {
            if (row.Npc.RowId == 0 ||
                !row.SatisfactionNpcParams.Any(p => p.SupplyIndex > 0 && supplyGroups.Contains((uint)p.SupplyIndex)))
                continue;

            if (row.Npc.ValueNullable is not { } npc || npc.Singular.IsEmpty)
                continue;

            var quest = row.QuestRequired.ValueNullable;
            deliveries.Add(new DeliverySource(
                TitleCase(npc.Singular.ExtractText()),
                quest is { Name.IsEmpty: false } q
                    ? new QuestLink(row.QuestRequired.RowId, q.Name.ExtractText())
                    : null));
        }

        return deliveries;
    }

    private List<CollectableTurnIn> BuildCollectableTurnIns(uint itemRowId)
    {
        // Several collectable shops list the same turn-in; identical
        // (level band, payout) entries collapse to one.
        var turnIns = new List<CollectableTurnIn>();
        foreach (var group in SubrowSheet<CollectablesShopItem>())
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId != itemRowId)
                    continue;

                var turnIn = new CollectableTurnIn(
                    sub.LevelMin,
                    sub.LevelMax,
                    sub.CollectablesShopRewardScrip.ValueNullable?.HighReward ?? 0);
                if (!turnIns.Contains(turnIn))
                    turnIns.Add(turnIn);
            }
        }

        return turnIns;
    }

    // For timeworn maps: max party size and dig-site count per zone. Per-site
    // flags are left out on purpose, since a zone has several candidate sites.
    // TreasureSpot subrows hang off the TreasureHuntRank row naming the map.
    private TreasureMapInfo? BuildTreasureMap(uint itemRowId)
    {
        var ranks = new List<TreasureHuntRank>();
        foreach (var rank in Sheet<TreasureHuntRank>())
        {
            if (rank.ItemName.RowId == itemRowId)
                ranks.Add(rank);
        }

        if (ranks.Count == 0)
            return null;

        // Some map items sit on two rank rows (Timeworn Leather Map); the
        // stray row's spots carry Location 0 and contribute nothing.
        var partySize = 0;
        var order = new List<string>();
        var counts = new Dictionary<string, int>();
        var spots = SubrowSheet<TreasureSpot>();
        foreach (var rank in ranks)
        {
            if (!spots.HasRow(rank.RowId))
                continue;

            var any = false;
            foreach (var spot in spots.GetRow(rank.RowId))
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
                partySize = Math.Max(partySize, rank.MaxPartySize);
        }

        return order.Count == 0
            ? null
            : new TreasureMapInfo(partySize, order.Select(z => new TreasureZone(z, counts[z])).ToList());
    }
}
