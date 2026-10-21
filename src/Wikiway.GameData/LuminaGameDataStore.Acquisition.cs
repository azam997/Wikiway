using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Item acquisition: the sources listed in an item card's ACQUISITION block.
// Built when the card is opened (GetItemDetails), never per search result.
// Each source type scans its own sheet for rows naming the item; most of
// those scans take well under a millisecond once the sheet is loaded. This
// file has recipes, gathering, fishing and ventures; the shops (gil, special
// and Grand Company) are in .Shops.cs. Only what the sheets state is listed;
// duty drops and treasure coffers aren't in the client data, so the wiki
// covers those.
public sealed partial class LuminaGameDataStore
{
    // Null when the sheets know no source at all.
    private ItemAcquisition? BuildAcquisition(uint itemRowId, uint gilPrice)
    {
        var recipes = BuildRecipes(itemRowId);
        var vendors = BuildVendors(itemRowId, gilPrice);
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

    // Recipes whose result is the item, with ingredients and master book.
    private List<RecipeSource> BuildRecipes(uint itemRowId)
    {
        var recipes = new List<RecipeSource>();
        foreach (var recipe in Sheet<Recipe>())
        {
            if (recipe.ItemResult.RowId != itemRowId)
                continue;

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

        return recipes;
    }

    // Gathering nodes (mining, logging, harvesting, quarrying) with their
    // level, map position and, for timed nodes, the Eorzea-time window.
    // GatheringItem sits between the item and the node templates
    // (GatheringPointBase) that list it.
    private List<GatheringSource> BuildGathering(uint itemRowId)
    {
        var gathering = new List<GatheringSource>();
        var gatheringItemIds = new HashSet<uint>();
        foreach (var row in Sheet<GatheringItem>())
        {
            if (row.Item.RowId == itemRowId)
                gatheringItemIds.Add(row.RowId);
        }

        if (gatheringItemIds.Count == 0)
            return gathering;

        var baseIds = new List<uint>();
        foreach (var pointBase in Sheet<GatheringPointBase>())
        {
            if (pointBase.Item.Any(entry => gatheringItemIds.Contains(entry.RowId)))
                baseIds.Add(pointBase.RowId);
        }

        var pointByBase = FirstPlacedPoints(baseIds);
        var seen = new HashSet<(string Type, string Zone, float X, float Y, string Window)>();
        foreach (var baseId in baseIds)
        {
            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(baseId);
            if (pointBase == null)
                continue;

            var pointId = pointByBase.GetValueOrDefault(baseId);
            var nodeType = pointBase.Value.GatheringType.ValueNullable?.Name.ExtractText() ?? "";
            var location = GatheringLocation(baseId, pointId);
            var window = GatheringTimeWindow(pointId);
            var key = location is { } loc
                ? (nodeType, loc.ZoneName, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1), window)
                : (nodeType, "", 0f, 0f, window);
            if (seen.Add(key))
                gathering.Add(new GatheringSource(nodeType, pointBase.Value.GatheringLevel, location, window));
        }

        return gathering;
    }

    // Node template -> the first placed GatheringPoint using it, which carries
    // the zone.
    private Dictionary<uint, uint> FirstPlacedPoints(List<uint> baseIds)
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var point in Sheet<GatheringPoint>())
        {
            if (point.GatheringPointBase.RowId != 0 && point.TerritoryType.RowId != 0 &&
                point.PlaceName.RowId != 0 && baseIds.Contains(point.GatheringPointBase.RowId))
                lookup.TryAdd(point.GatheringPointBase.RowId, point.RowId);
        }

        return lookup;
    }

    // Every item some gathering node yields. Only the name index uses this, to
    // tag those names Gatherable, so it's built once while the index is.
    private HashSet<uint> GatherableItemIds()
    {
        var itemByGatheringItem = new Dictionary<uint, uint>();
        foreach (var row in Sheet<GatheringItem>())
        {
            if (row.Item.RowId != 0)
                itemByGatheringItem.TryAdd(row.RowId, row.Item.RowId);
        }

        var items = new HashSet<uint>();
        foreach (var pointBase in Sheet<GatheringPointBase>())
        {
            foreach (var entry in pointBase.Item)
            {
                if (itemByGatheringItem.TryGetValue(entry.RowId, out var itemId))
                    items.Add(itemId);
            }
        }

        return items;
    }

    // "Unspoiled · 9:00-12:00 ET" or "Ephemeral · 16:00-20:00 ET"; empty for
    // nodes that are always up.
    private string GatheringTimeWindow(uint pointRowId)
    {
        if (pointRowId == 0)
            return "";

        var transient = Sheet<GatheringPointTransient>().GetRowOrDefault(pointRowId);
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
    private MapLocation? GatheringLocation(uint baseRowId, uint pointRowId)
    {
        if (pointRowId == 0)
            return null;

        var point = Sheet<GatheringPoint>().GetRowOrDefault(pointRowId);
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
    // (0-2048), not world units. Spots without a zone or place name are
    // skipped.
    private List<FishingSource> BuildFishing(uint itemRowId)
    {
        var fishing = new List<FishingSource>();
        foreach (var spot in Sheet<FishingSpot>())
        {
            if (spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0 ||
                !spot.Item.Any(item => item.RowId == itemRowId))
                continue;

            if (spot.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                continue;

            fishing.Add(new FishingSource(
                place.Name.ExtractText(),
                spot.GatheringLevel,
                FishingLocation(spot.TerritoryType.ValueNullable, spot.X, spot.Z),
                Spearfishing: false));
        }

        // Spearfishing bases fill their GatheringPointBase.Item slots with
        // SpearfishingItem row ids (20000+), disjoint from GatheringItem ids,
        // so the gathering scan above never sees them.
        var spearRows = new HashSet<uint>();
        foreach (var row in Sheet<SpearfishingItem>())
        {
            if (row.Item.RowId == itemRowId)
                spearRows.Add(row.RowId);
        }

        if (spearRows.Count == 0)
            return fishing;

        foreach (var spot in Sheet<SpearfishingNotebook>())
        {
            if (spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0)
                continue;

            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(spot.GatheringPointBase.RowId);
            if (pointBase == null || !pointBase.Value.Item.Any(entry => spearRows.Contains(entry.RowId)))
                continue;

            if (spot.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                continue;

            fishing.Add(new FishingSource(
                place.Name.ExtractText(),
                spot.GatheringLevel,
                FishingLocation(spot.TerritoryType.ValueNullable, spot.X, spot.Y),
                Spearfishing: true));
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

    // The fisher's log lore text (FishParameter), shown under the fishing
    // lines.
    private string FishNote(uint itemRowId)
    {
        foreach (var row in Sheet<FishParameter>())
        {
            if (row.Item.RowId == itemRowId && !row.Text.IsEmpty)
                return row.Text.ExtractText();
        }

        return "";
    }

    // Retainer ventures that bring back the item, with the quantity range a
    // venture can return.
    private List<VentureSource> BuildVentures(uint itemRowId)
    {
        var ventures = new List<VentureSource>();
        var normalIds = new List<uint>();
        foreach (var normal in Sheet<RetainerTaskNormal>())
        {
            if (normal.Item.RowId == itemRowId)
                normalIds.Add(normal.RowId);
        }

        if (normalIds.Count == 0)
            return ventures;

        // RetainerTask.Task points at RetainerTaskNormal (or RetainerTaskRandom
        // for the exploration ventures, which never award a specific item).
        // The lowest task row is the one shown.
        var taskByNormal = new Dictionary<uint, uint>();
        foreach (var task in Sheet<RetainerTask>())
        {
            if (task.IsRandom || task.Task.RowId == 0 || !normalIds.Contains(task.Task.RowId))
                continue;

            if (!taskByNormal.TryGetValue(task.Task.RowId, out var existing) || task.RowId < existing)
                taskByNormal[task.Task.RowId] = task.RowId;
        }

        foreach (var normalId in normalIds)
        {
            if (!taskByNormal.TryGetValue(normalId, out var taskId))
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
