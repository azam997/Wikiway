using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

public sealed class LuminaGameDataStore : IGameDataStore
{
    private const byte LevelObjectTypeEventNpc = 8;
    private const uint ContentTypeQuestBattles = 7;
    private const uint ContentTypeMaskedCarnivale = 27;

    private readonly Lumina.GameData gameData;
    private readonly Lock levelLock = new();
    private readonly Lock dutyLock = new();
    private readonly Lock acquisitionLock = new();
    private Dictionary<uint, Level>? npcLevels;
    private Dictionary<uint, uint>? dutyByTerritory;
    private Dictionary<uint, List<uint>>? recipesByItem;
    private Dictionary<uint, List<uint>>? shopsByItem;
    private Dictionary<uint, List<uint>>? npcsByShop;

    public LuminaGameDataStore(Lumina.GameData gameData)
    {
        this.gameData = gameData;
    }

    public IReadOnlyList<NameIndexEntry> GetAllNames()
    {
        var names = new List<NameIndexEntry>(80_000);

        foreach (var row in gameData.GetExcelSheet<Item>()!)
            AddName(names, EntityKind.Item, row.RowId, row.Name.ExtractText());

        foreach (var row in gameData.GetExcelSheet<ENpcResident>()!)
            AddName(names, EntityKind.Npc, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Quest>()!)
            AddName(names, EntityKind.Quest, row.RowId, row.Name.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Mount>()!)
            AddName(names, EntityKind.Mount, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Companion>()!)
            AddName(names, EntityKind.Minion, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Achievement>()!)
            AddName(names, EntityKind.Achievement, row.RowId, row.Name.ExtractText());

        foreach (var row in gameData.GetExcelSheet<ContentFinderCondition>()!)
            AddName(names, EntityKind.Duty, row.RowId, row.Name.ExtractText());

        return names;
    }

    private static void AddName(List<NameIndexEntry> names, EntityKind kind, uint rowId, string name)
    {
        if (name.Length > 0)
            names.Add(new NameIndexEntry(kind, rowId, name.ToLowerInvariant()));
    }

    public ItemEntity? GetItem(uint rowId)
    {
        var row = gameData.GetExcelSheet<Item>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new ItemEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
            row.Value.Description.ExtractText(),
            row.Value.ItemSearchCategory.RowId != 0,
            row.Value.Icon,
            BuildAcquisition(rowId, row.Value.PriceMid));
    }

    private ItemAcquisition? BuildAcquisition(uint itemRowId, uint gilPrice)
    {
        if (recipesByItem == null || shopsByItem == null || npcsByShop == null)
        {
            lock (acquisitionLock)
            {
                recipesByItem ??= BuildRecipeLookup();
                shopsByItem ??= BuildShopItemLookup();
                npcsByShop ??= BuildShopNpcLookup();
            }
        }

        var recipes = new List<RecipeSource>();
        if (recipesByItem.TryGetValue(itemRowId, out var recipeIds))
        {
            var sheet = gameData.GetExcelSheet<Recipe>()!;
            foreach (var recipeId in recipeIds)
            {
                var recipe = sheet.GetRow(recipeId);
                var ingredients = new List<string>();
                for (var i = 0; i < recipe.Ingredient.Count; i++)
                {
                    if (recipe.Ingredient[i].RowId != 0 &&
                        recipe.Ingredient[i].ValueNullable is { } ingredient &&
                        !ingredient.Name.IsEmpty)
                        ingredients.Add($"{recipe.AmountIngredient[i]}x {ingredient.Name.ExtractText()}");
                }

                recipes.Add(new RecipeSource(
                    recipe.CraftType.ValueNullable?.Name.ExtractText() ?? "",
                    recipe.RecipeLevelTable.ValueNullable?.ClassJobLevel ?? 0,
                    ingredients));
            }
        }

        var vendors = new List<VendorSource>();
        if (shopsByItem.TryGetValue(itemRowId, out var shopIds))
        {
            var seen = new HashSet<(string Name, uint MapId, float X, float Y)>();
            foreach (var shopId in shopIds)
            {
                if (!npcsByShop.TryGetValue(shopId, out var npcIds))
                    continue;

                foreach (var npcId in npcIds)
                {
                    var resident = gameData.GetExcelSheet<ENpcResident>()!.GetRowOrDefault(npcId);
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

        return recipes.Count == 0 && vendors.Count == 0 ? null : new ItemAcquisition(recipes, vendors);
    }

    private Dictionary<uint, List<uint>> BuildRecipeLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var recipe in gameData.GetExcelSheet<Recipe>()!)
        {
            if (recipe.ItemResult.RowId != 0)
                Add(lookup, recipe.ItemResult.RowId, recipe.RowId);
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildShopItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var group in gameData.GetSubrowExcelSheet<GilShopItem>()!)
        {
            foreach (var sub in group)
            {
                if (sub.Item.RowId != 0)
                    Add(lookup, sub.Item.RowId, group.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildShopNpcLookup()
    {
        // Gil shop event ids live in the 0x40000 block of ENpcData.
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var npcBase in gameData.GetExcelSheet<ENpcBase>()!)
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x40000 and < 0x50000)
                    Add(lookup, handler.RowId, npcBase.RowId);
            }
        }

        return lookup;
    }

    private static void Add(Dictionary<uint, List<uint>> lookup, uint key, uint value)
    {
        if (!lookup.TryGetValue(key, out var list))
            lookup[key] = list = [];
        list.Add(value);
    }

    public NpcEntity? GetNpc(uint rowId)
    {
        var row = gameData.GetExcelSheet<ENpcResident>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new NpcEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), FindLocation(rowId),
            CountEventHandlers(rowId));
    }

    private int CountEventHandlers(uint npcRowId)
    {
        var row = gameData.GetExcelSheet<ENpcBase>()!.GetRowOrDefault(npcRowId);
        if (row == null)
            return 0;

        var count = 0;
        foreach (var handler in row.Value.ENpcData)
        {
            if (handler.RowId != 0)
                count++;
        }

        return count;
    }

    public QuestEntity? GetQuest(uint rowId)
    {
        var row = gameData.GetExcelSheet<Quest>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var quest = row.Value;
        var prerequisites = new List<QuestLink>();
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && previous.ValueNullable is { } prev && !prev.Name.IsEmpty)
                prerequisites.Add(new QuestLink(previous.RowId, prev.Name.ExtractText()));
        }

        return new QuestEntity(
            rowId,
            quest.Name.ExtractText(),
            quest.ClassJobLevel.FirstOrDefault(),
            quest.JournalGenre.ValueNullable?.Name.ExtractText() ?? "",
            prerequisites);
    }

    public MountEntity? GetMount(uint rowId)
    {
        var row = gameData.GetExcelSheet<Mount>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MountEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon);
    }

    public MinionEntity? GetMinion(uint rowId)
    {
        var row = gameData.GetExcelSheet<Companion>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MinionEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon);
    }

    public AchievementEntity? GetAchievement(uint rowId)
    {
        var row = gameData.GetExcelSheet<Achievement>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new AchievementEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            row.Value.AchievementCategory.ValueNullable?.Name.ExtractText() ?? "");
    }

    public DutyEntity? GetDuty(uint rowId)
    {
        var row = gameData.GetExcelSheet<ContentFinderCondition>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var duty = row.Value;

        // ContentMemberType is all zeroes for single-player content, so party size
        // can't identify it; the content type can - quest battles and the Carnivale.
        var solo = duty.ContentType.RowId is ContentTypeQuestBattles or ContentTypeMaskedCarnivale;

        return new DutyEntity(
            rowId,
            duty.Name.ExtractText(),
            duty.ContentType.ValueNullable?.Name.ExtractText() ?? "",
            duty.ClassJobLevelRequired,
            duty.ItemLevelRequired,
            solo,
            duty.HighEndDuty,
            duty.TerritoryType.RowId);
    }

    public DutyEntity? FindDutyByTerritory(uint territoryTypeId)
    {
        var lookup = dutyByTerritory;
        if (lookup == null)
        {
            lock (dutyLock)
            {
                lookup = dutyByTerritory ??= BuildDutyTerritoryLookup();
            }
        }

        return lookup.TryGetValue(territoryTypeId, out var rowId) ? GetDuty(rowId) : null;
    }

    private Dictionary<uint, uint> BuildDutyTerritoryLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var duty in gameData.GetExcelSheet<ContentFinderCondition>()!)
        {
            if (duty.TerritoryType.RowId != 0 && !duty.Name.IsEmpty)
                lookup.TryAdd(duty.TerritoryType.RowId, duty.RowId);
        }

        return lookup;
    }

    private MapLocation? FindLocation(uint npcRowId)
    {
        var levels = npcLevels;
        if (levels == null)
        {
            lock (levelLock)
            {
                levels = npcLevels ??= BuildNpcLevelLookup();
            }
        }

        if (!levels.TryGetValue(npcRowId, out var level))
            return null;

        var map = level.Map.ValueNullable;
        var territory = level.Territory.ValueNullable;
        if (map == null || territory == null)
            return null;

        var zone = territory.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "";

        // World Z is the north-south axis; map Y comes from it, not from world Y (height).
        var mapX = MapCoordConverter.ToMapCoord(level.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(level.Z, map.Value.SizeFactor, map.Value.OffsetY);

        return new MapLocation(territory.Value.RowId, map.Value.RowId, mapX, mapY, zone);
    }

    private Dictionary<uint, Level> BuildNpcLevelLookup()
    {
        // A full Level scan is ~100k rows; do it once and keep the ENpc entries.
        var lookup = new Dictionary<uint, Level>();
        foreach (var level in gameData.GetExcelSheet<Level>()!)
        {
            if (level.Type == LevelObjectTypeEventNpc)
                lookup.TryAdd(level.Object.RowId, level);
        }

        return lookup;
    }

    // Singular names come lowercased ("momodi modi"); the UI wants display casing.
    private static string TitleCase(string name) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
