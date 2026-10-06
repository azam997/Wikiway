using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Item acquisition: turns the reverse indexes in .AcquisitionIndex.cs into
// the sources listed in an item card's ACQUISITION block. Only what the sheets
// state is listed; duty drops and treasure coffers aren't in the client data,
// so the wiki covers those.
public sealed partial class LuminaGameDataStore
{
    // Null when the sheets know no source at all.
    private ItemAcquisition? BuildAcquisition(uint itemRowId, uint gilPrice)
    {
        var recipes = new List<RecipeSource>();
        if (acquisitionIndex.Value.RecipesByItem.TryGetValue(itemRowId, out var recipeIds))
        {
            var sheet = Sheet<Recipe>();
            foreach (var recipeId in recipeIds)
            {
                var recipe = sheet.GetRow(recipeId);
                var ingredients = new List<ItemAmount>();
                for (var i = 0; i < recipe.Ingredient.Count; i++)
                {
                    if (recipe.Ingredient[i].RowId != 0 &&
                        recipe.Ingredient[i].ValueNullable is { } ingredient &&
                        !ingredient.Name.IsEmpty)
                        ingredients.Add(new ItemAmount(ingredient.Name.ExtractText(), recipe.AmountIngredient[i]));
                }

                recipes.Add(new RecipeSource(
                    recipe.CraftType.ValueNullable?.Name.ExtractText() ?? "",
                    recipe.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0,
                    ingredients,
                    recipe.SecretRecipeBook.ValueNullable?.Name.ExtractText() ?? ""));
            }
        }

        var vendors = new List<VendorSource>();
        if (acquisitionIndex.Value.ShopsByItem.TryGetValue(itemRowId, out var shopIds))
        {
            var seen = new HashSet<(string Name, uint MapId, float X, float Y)>();
            foreach (var shopId in shopIds)
            {
                if (!acquisitionIndex.Value.NpcsByShop.TryGetValue(shopId, out var npcIds))
                    continue;

                foreach (var npcId in npcIds)
                {
                    var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
                    if (resident == null || resident.Value.Singular.IsEmpty)
                        continue;

                    var name = TitleCase(resident.Value.Singular.ExtractText());
                    var location = FindLocation(npcId);
                    var key = location is { } loc
                        ? (name.ToLowerInvariant(), loc.MapId, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1))
                        : (name.ToLowerInvariant(), 0u, 0f, 0f);
                    if (seen.Add(key))
                        vendors.Add(new VendorSource(name, location, gilPrice));
                }
            }

            // Housing-district copies have no Level row; hide them when a
            // located copy of the same vendor exists.
            var located = vendors.Where(v => v.Location != null)
                .Select(v => v.NpcName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            vendors.RemoveAll(v => v.Location == null && located.Contains(v.NpcName));
        }

        var exchanges = BuildExchanges(itemRowId);
        var gathering = BuildGathering(itemRowId);
        var fishing = BuildFishing(itemRowId);
        var sealVendors = BuildSealVendors(itemRowId);
        var ventures = BuildVentures(itemRowId);
        var fishingNote = fishing.Count > 0 ? FishNote(itemRowId) : "";

        return recipes.Count == 0 && vendors.Count == 0 && exchanges.Count == 0 && gathering.Count == 0 &&
               fishing.Count == 0 && sealVendors.Count == 0 && ventures.Count == 0
            ? null
            : new ItemAcquisition(recipes, vendors, exchanges, gathering, fishing, sealVendors, ventures, fishingNote);
    }

    // Special-shop exchanges (tomestones, scrips, item trades): one line per
    // shop and NPC, deduplicated by shop name and NPC position.
    private List<ExchangeSource> BuildExchanges(uint itemRowId)
    {
        var exchanges = new List<ExchangeSource>();
        if (!acquisitionIndex.Value.SpecialShopsByItem.TryGetValue(itemRowId, out var shopIds))
            return exchanges;

        var seen = new HashSet<(string Shop, string Npc, uint MapId, float X, float Y)>();
        foreach (var shopId in shopIds)
        {
            var shop = Sheet<SpecialShop>().GetRowOrDefault(shopId);
            if (shop == null || !acquisitionIndex.Value.NpcsBySpecialShop.TryGetValue(shopId, out var npcIds))
                continue;

            var costs = BuildExchangeCosts(shop.Value, itemRowId);
            if (costs.Count == 0)
                continue;

            var shopName = shop.Value.Name.ExtractText();
            foreach (var npcId in npcIds)
            {
                var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
                if (resident == null || resident.Value.Singular.IsEmpty)
                    continue;

                var npcName = TitleCase(resident.Value.Singular.ExtractText());
                var location = FindLocation(npcId);
                var key = location is { } loc
                    ? (shopName.ToLowerInvariant(), npcName.ToLowerInvariant(), loc.MapId, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1))
                    : (shopName.ToLowerInvariant(), npcName.ToLowerInvariant(), 0u, 0f, 0f);
                if (seen.Add(key))
                    exchanges.Add(new ExchangeSource(shopName.Length > 0 ? shopName : npcName, npcName, location, costs));
            }
        }

        return exchanges;
    }

    private static List<IReadOnlyList<ItemAmount>> BuildExchangeCosts(SpecialShop shop, uint itemRowId)
    {
        // Each shop entry is one offer; its cost slots are all required together.
        // Tomestone stand-in slots resolve to no named item and are skipped.
        var costs = new List<IReadOnlyList<ItemAmount>>();
        foreach (var entry in shop.Item)
        {
            if (!entry.ReceiveItems.Any(r => r.Item.RowId == itemRowId))
                continue;

            var parts = new List<ItemAmount>();
            foreach (var cost in entry.ItemCosts)
            {
                if (cost.ItemCost.ValueNullable is { } costItem && !costItem.Name.IsEmpty)
                    parts.Add(new ItemAmount(costItem.Name.ExtractText(), cost.CurrencyCost));
            }

            if (parts.Count > 0)
                costs.Add(parts);
        }

        return costs;
    }

    // Gathering nodes (mining, logging, harvesting, quarrying) with their
    // level, map position and, for timed nodes, the Eorzea-time window.
    private List<GatheringSource> BuildGathering(uint itemRowId)
    {
        var gathering = new List<GatheringSource>();
        if (!acquisitionIndex.Value.GatheringBasesByItem.TryGetValue(itemRowId, out var baseIds))
            return gathering;

        var seen = new HashSet<(string Type, string Zone, float X, float Y, string Window)>();
        foreach (var baseId in baseIds)
        {
            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(baseId);
            if (pointBase == null)
                continue;

            var nodeType = pointBase.Value.GatheringType.ValueNullable?.Name.ExtractText() ?? "";
            var location = GatheringLocation(baseId);
            var window = GatheringTimeWindow(baseId);
            var key = location is { } loc
                ? (nodeType, loc.ZoneName, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1), window)
                : (nodeType, "", 0f, 0f, window);
            if (seen.Add(key))
                gathering.Add(new GatheringSource(nodeType, pointBase.Value.GatheringLevel, location, window));
        }

        return gathering;
    }

    // "Unspoiled · 9:00-12:00 ET" or "Ephemeral · 16:00-20:00 ET"; empty for
    // nodes that are always up.
    private string GatheringTimeWindow(uint baseRowId)
    {
        if (!acquisitionIndex.Value.GatheringPointByBase.TryGetValue(baseRowId, out var pointId))
            return "";

        var transient = Sheet<GatheringPointTransient>().GetRowOrDefault(pointId);
        if (transient == null)
            return "";

        if (transient.Value.GatheringRarePopTimeTable.ValueNullable is { } table)
        {
            var windows = new List<string>();
            for (var i = 0; i < table.StartTime.Count && i < table.Duration.Count; i++)
            {
                if (table.StartTime[i] == EorzeaClock.Unused)
                    continue;

                var start = EorzeaClock.ToMinutes(table.StartTime[i]);
                windows.Add($"{EorzeaClock.Label(start)}-{EorzeaClock.Label(start + EorzeaClock.ToMinutes(table.Duration[i]))}");
            }

            if (windows.Count > 0)
                return $"Unspoiled · {string.Join(", ", windows)} ET";
        }

        var ephemeralStart = transient.Value.EphemeralStartTime;
        var ephemeralEnd = transient.Value.EphemeralEndTime;
        if (ephemeralStart == EorzeaClock.Unused || ephemeralStart == ephemeralEnd)
            return "";

        var end = ephemeralEnd == 0 ? EorzeaClock.Day : EorzeaClock.ToMinutes(ephemeralEnd);
        return $"Ephemeral · {EorzeaClock.Label(EorzeaClock.ToMinutes(ephemeralStart))}-{EorzeaClock.Label(end)} ET";
    }

    // ExportedGatheringPoint holds the node's world position, converted to map
    // coordinates like any Level row.
    private MapLocation? GatheringLocation(uint baseRowId)
    {
        if (!acquisitionIndex.Value.GatheringPointByBase.TryGetValue(baseRowId, out var pointId))
            return null;

        var point = Sheet<GatheringPoint>().GetRowOrDefault(pointId);
        var territory = point?.TerritoryType.ValueNullable;
        var map = territory?.Map.ValueNullable;
        var exported = Sheet<ExportedGatheringPoint>().GetRowOrDefault(baseRowId);
        if (territory == null || map == null || exported == null)
            return null;

        var mapX = MapCoordConverter.ToMapCoord(exported.Value.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(exported.Value.Y, map.Value.SizeFactor, map.Value.OffsetY);
        return Locate(territory.Value, map.Value, mapX, mapY);
    }

    // Fishing holes and spearfishing spots. Both sheets store map-image pixels
    // (0-2048), not world units.
    private List<FishingSource> BuildFishing(uint itemRowId)
    {
        var fishing = new List<FishingSource>();
        if (acquisitionIndex.Value.FishingSpotsByItem.TryGetValue(itemRowId, out var spotIds))
        {
            foreach (var spotId in spotIds)
            {
                var spot = Sheet<FishingSpot>().GetRowOrDefault(spotId);
                if (spot == null || spot.Value.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                    continue;

                fishing.Add(new FishingSource(
                    place.Name.ExtractText(),
                    spot.Value.GatheringLevel,
                    FishingLocation(spot.Value.TerritoryType.ValueNullable, spot.Value.X, spot.Value.Z),
                    Spearfishing: false));
            }
        }

        if (acquisitionIndex.Value.SpearfishingSpotsByItem.TryGetValue(itemRowId, out var notebookIds))
        {
            foreach (var notebookId in notebookIds)
            {
                var spot = Sheet<SpearfishingNotebook>().GetRowOrDefault(notebookId);
                if (spot == null || spot.Value.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                    continue;

                fishing.Add(new FishingSource(
                    place.Name.ExtractText(),
                    spot.Value.GatheringLevel,
                    FishingLocation(spot.Value.TerritoryType.ValueNullable, spot.Value.X, spot.Value.Y),
                    Spearfishing: true));
            }
        }

        return fishing;
    }

    private MapLocation? FishingLocation(TerritoryType? territory, int pixelX, int pixelY)
    {
        var map = territory?.Map.ValueNullable;
        if (territory == null || map == null)
            return null;

        return Locate(
            territory.Value,
            map.Value,
            MapCoordConverter.FromMapPixel(pixelX, map.Value.SizeFactor),
            MapCoordConverter.FromMapPixel(pixelY, map.Value.SizeFactor));
    }

    // The fisher's log lore text, shown under the fishing lines.
    private string FishNote(uint itemRowId)
    {
        if (!acquisitionIndex.Value.FishNoteByItem.TryGetValue(itemRowId, out var paramId))
            return "";

        var row = Sheet<FishParameter>().GetRowOrDefault(paramId);
        return row?.Text.ExtractText() ?? "";
    }

    // Grand Company quartermasters selling the item for seals, with the minimum
    // rank when there is one.
    private List<SealVendorSource> BuildSealVendors(uint itemRowId)
    {
        var vendors = new List<SealVendorSource>();
        if (!acquisitionIndex.Value.SealOffersByItem.TryGetValue(itemRowId, out var offers))
            return vendors;

        var seen = new HashSet<GcSealOffer>();
        foreach (var offer in offers)
        {
            if (!seen.Add(offer) || !acquisitionIndex.Value.QuartermasterByCompany.TryGetValue(offer.GrandCompany, out var npcId))
                continue;

            var resident = Sheet<ENpcResident>().GetRowOrDefault(npcId);
            if (resident == null || resident.Value.Singular.IsEmpty)
                continue;

            vendors.Add(new SealVendorSource(
                TitleCase(resident.Value.Singular.ExtractText()),
                FindLocation(npcId),
                offer.Seals,
                RankName(offer.GrandCompany, offer.Rank)));
        }

        return vendors;
    }

    // Rank display names live in per-company text sheets; the male and female
    // variants carry the same strings. Rank 1 means any member, not worth a label.
    private string RankName(uint grandCompany, uint rankRowId)
    {
        if (rankRowId <= 1)
            return "";

        return grandCompany switch
        {
            1 => Sheet<GCRankLimsaMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            2 => Sheet<GCRankGridaniaMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            3 => Sheet<GCRankUldahMaleText>().GetRowOrDefault(rankRowId)?.Singular.ExtractText() ?? "",
            _ => "",
        };
    }

    // Retainer ventures that bring back the item, with the quantity range a
    // venture can return.
    private List<VentureSource> BuildVentures(uint itemRowId)
    {
        var ventures = new List<VentureSource>();
        if (!acquisitionIndex.Value.VenturesByItem.TryGetValue(itemRowId, out var normalIds))
            return ventures;

        foreach (var normalId in normalIds)
        {
            if (!acquisitionIndex.Value.VentureTaskByNormal.TryGetValue(normalId, out var taskId))
                continue;

            var task = Sheet<RetainerTask>().GetRowOrDefault(taskId);
            var normal = Sheet<RetainerTaskNormal>().GetRowOrDefault(normalId);
            if (task == null || normal == null)
                continue;

            var quantities = normal.Value.Quantity.Where(q => q > 0).ToList();
            ventures.Add(new VentureSource(
                task.Value.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "",
                task.Value.RetainerLevel,
                task.Value.VentureCost,
                quantities.Count == 0 ? ""
                    : quantities[0] == quantities[^1] ? $"{quantities[0]}"
                    : $"{quantities[0]}-{quantities[^1]}"));
        }

        return ventures;
    }
}
