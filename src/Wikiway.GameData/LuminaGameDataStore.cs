using Lumina.Excel;
using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;

namespace Wikiway.GameData;

public sealed class LuminaGameDataStore : IGameDataStore
{
    private const byte LevelObjectTypeEventNpc = 8;

    // Solo := quest battles or Masked Carnivale. ContentMemberType can't tell:
    // quest battles carry all-zero role counts and named CFC party sizes are
    // only ever 0/4/8 (probed against 7.3 sheets; 116 solo rows). The canary
    // band test trips if a patch introduces a new solo content type.
    private const uint ContentTypeQuestBattles = 7;
    private const uint ContentTypeMaskedCarnivale = 27;
    private const byte ContentLinkTypeInstanceContent = 1;
    private const byte ContentLinkTypePublicContent = 3;

    // Field-operation zones: Diadem, Eureka, Save the Queen, Occult Crescent.
    // Public-content link alone is too broad (GATEs are public content too).
    private static readonly uint[] AreaContentTypes = [16, 26, 29, 38];

    // These field-area CFC rows carry no quest-typed UnlockCriteria; the real
    // gate quests were probe-verified against the sheets and are canary-pinned.
    private static readonly Dictionary<uint, uint> FieldAreaUnlockQuests = new()
    {
        [735] = 69477, // the Bozjan Southern Front <- Where Eagles Nest
        [742] = 69208, // the Diadem <- Towards the Firmament
        [753] = 69208, // the Diadem <- Towards the Firmament
        [760] = 69562, // Delubrum Reginae <- Fit for a Queen
        [761] = 69562, // Delubrum Reginae (Savage) <- Fit for a Queen
        [778] = 69620, // Zadnor <- A New Playing Field
    };

    // Unlockable zones with no ContentFinderCondition row; synthetic ids sit
    // far above the CFC sheet so they can never collide with real rows.
    private const uint CuratedZoneBase = 1_000_000;

    private static readonly (uint RowId, string Name, string Kind, uint QuestId)[] CuratedZones =
    [
        (CuratedZoneBase + 0, "The Firmament", "Ishgardian Restoration", 69208),  // Towards the Firmament
        (CuratedZoneBase + 1, "The Gold Saucer", "Gold Saucer", 65970),           // It Could Happen to You
        (CuratedZoneBase + 2, "Island Sanctuary", "Island Sanctuary", 70179),     // Seeking Sanctuary
    ];

    private readonly Lumina.GameData gameData;
    private readonly Lock levelLock = new();
    private readonly Lock dutyLock = new();
    private readonly Lock unlockLock = new();
    private readonly Lock acquisitionLock = new();
    private Dictionary<uint, Level>? npcLevels;
    private Dictionary<uint, uint>? dutyByTerritory;
    private Dictionary<uint, uint>? unlockQuestByInstance;
    private Dictionary<uint, List<uint>>? recipesByItem;
    private Dictionary<uint, List<uint>>? shopsByItem;
    private Dictionary<uint, List<uint>>? npcsByShop;
    private Dictionary<uint, List<uint>>? specialShopsByItem;
    private Dictionary<uint, List<uint>>? npcsBySpecialShop;
    private Dictionary<uint, List<uint>>? gatheringBasesByItem;
    private Dictionary<uint, uint>? gatheringPointByBase;

    public LuminaGameDataStore(Lumina.GameData gameData)
    {
        this.gameData = gameData;
    }

    // The `!` alternative turns a renamed sheet after a game patch into a bare
    // NullReferenceException deep in a background task; name the sheet instead.
    private ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T> =>
        gameData.GetExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    private SubrowExcelSheet<T> SubrowSheet<T>() where T : struct, IExcelSubrow<T> =>
        gameData.GetSubrowExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    public void WarmAll(CancellationToken ct)
    {
        EnsureAcquisitionLookups();
        ct.ThrowIfCancellationRequested();
        UnlockQuestsByInstance();
        ct.ThrowIfCancellationRequested();
        NpcLevels();
        ct.ThrowIfCancellationRequested();
        DutyTerritoryLookup();
    }

    public IReadOnlyList<NameIndexEntry> GetAllNames(CancellationToken ct = default)
    {
        var names = new List<NameIndexEntry>(80_000);

        // Gatherables carry their own kind so the Gathering lens can scope to
        // them; the Items lens searches both kinds.
        var gatherable = GatheringBases();
        foreach (var row in Sheet<Item>())
            AddName(names, gatherable.ContainsKey(row.RowId) ? EntityKind.Gatherable : EntityKind.Item,
                row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<ENpcResident>())
            AddName(names, EntityKind.Npc, row.RowId, row.Singular.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Quest>())
            AddName(names, EntityKind.Quest, row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Mount>())
            AddName(names, EntityKind.Mount, row.RowId, row.Singular.ExtractText());

        foreach (var row in Sheet<Companion>())
            AddName(names, EntityKind.Minion, row.RowId, row.Singular.ExtractText());

        foreach (var row in Sheet<Achievement>())
            AddName(names, EntityKind.Achievement, row.RowId, row.Name.ExtractText());

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<ContentFinderCondition>())
            AddName(names, IsUnlockable(row) ? EntityKind.Unlockable : EntityKind.Duty, row.RowId, row.Name.ExtractText());

        foreach (var zone in CuratedZones)
            AddName(names, EntityKind.Unlockable, zone.RowId, zone.Name);

        return names;
    }

    public string? GetItemName(uint rowId)
    {
        var row = Sheet<Item>().GetRowOrDefault(rowId);
        return row == null || row.Value.Name.IsEmpty ? null : row.Value.Name.ExtractText();
    }

    public string? GetNpcName(uint rowId)
    {
        var row = Sheet<ENpcResident>().GetRowOrDefault(rowId);
        return row == null || row.Value.Singular.IsEmpty ? null : TitleCase(row.Value.Singular.ExtractText());
    }

    public string? FindSoloDutyName(uint territoryTypeId)
    {
        // Game-thread callback (TerritoryChanged): never build or wait on the
        // lazy lookup here. Before WarmAll has built it, skip the toast.
        if (dutyByTerritory is not { } lookup || !lookup.TryGetValue(territoryTypeId, out var rowId))
            return null;

        var row = Sheet<ContentFinderCondition>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return row.Value.ContentType.RowId is ContentTypeQuestBattles or ContentTypeMaskedCarnivale
            ? row.Value.Name.ExtractText()
            : null;
    }

    private bool IsUnlockable(ContentFinderCondition row)
    {
        var questId = ResolveUnlockQuestId(row);
        return questId != 0 && !IsMainScenario(questId);
    }

    private uint ResolveUnlockQuestId(ContentFinderCondition row)
    {
        if (row.UnlockCriteria.Is<Quest>() &&
            row.UnlockCriteria.GetValueOrDefault<Quest>() is { } unlock && !unlock.Name.IsEmpty)
            return row.UnlockCriteria.RowId;

        if (FieldAreaUnlockQuests.TryGetValue(row.RowId, out var curated))
            return curated;

        // Most instanced content carries no UnlockCriteria; its gate quest is
        // named on the quest side instead, via INSTANCEDUNGEON script args.
        return row.ContentLinkType == ContentLinkTypeInstanceContent &&
               UnlockQuestsByInstance().TryGetValue(row.Content.RowId, out var scripted)
            ? scripted
            : 0;
    }

    private Dictionary<uint, uint> UnlockQuestsByInstance()
    {
        var lookup = unlockQuestByInstance;
        if (lookup == null)
        {
            lock (unlockLock)
            {
                lookup = unlockQuestByInstance ??= BuildInstanceUnlockLookup();
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildInstanceUnlockLookup()
    {
        // Several quests can reference one instance (relic re-checks, later
        // chains); the lowest quest id is the original unlock.
        var lookup = new Dictionary<uint, uint>();
        foreach (var quest in Sheet<Quest>())
        {
            if (quest.Name.IsEmpty)
                continue;

            foreach (var param in quest.QuestParams)
            {
                if (param.ScriptArg == 0)
                    continue;

                if (!IsInstanceUnlockInstruction(param.ScriptInstruction.ExtractText()))
                    continue;

                if (!lookup.TryGetValue(param.ScriptArg, out var existing) || quest.RowId < existing)
                    lookup[param.ScriptArg] = quest.RowId;
            }
        }

        return lookup;
    }

    // "INSTANCEDUNGEON", optionally slot-numbered; named variants like
    // "INSTANCEDUNGEON_W" are labels, not unlock args.
    private static bool IsInstanceUnlockInstruction(string text)
    {
        const string stem = "INSTANCEDUNGEON";
        if (!text.StartsWith(stem, StringComparison.Ordinal))
            return false;

        for (var i = stem.Length; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
                return false;
        }

        return true;
    }

    private bool IsMainScenario(uint questRowId)
    {
        var quest = Sheet<Quest>().GetRowOrDefault(questRowId);
        return IsMainScenario(
            quest?.JournalGenre.ValueNullable?.JournalCategory.ValueNullable?.Name.ExtractText() ?? "");
    }

    private static bool IsMainScenario(string journalCategory) =>
        journalCategory.Contains("Main Scenario", StringComparison.OrdinalIgnoreCase);

    private static void AddName(List<NameIndexEntry> names, EntityKind kind, uint rowId, string name)
    {
        if (name.Length > 0)
            names.Add(new NameIndexEntry(kind, rowId, name.ToLowerInvariant()));
    }

    public ItemEntity? GetItem(uint rowId)
    {
        var row = Sheet<Item>().GetRowOrDefault(rowId);
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

    private void EnsureAcquisitionLookups()
    {
        if (recipesByItem == null || shopsByItem == null || npcsByShop == null ||
            specialShopsByItem == null || npcsBySpecialShop == null ||
            gatheringBasesByItem == null || gatheringPointByBase == null)
        {
            lock (acquisitionLock)
            {
                recipesByItem ??= BuildRecipeLookup();
                shopsByItem ??= BuildShopItemLookup();
                npcsByShop ??= BuildShopNpcLookup();
                specialShopsByItem ??= BuildSpecialShopItemLookup();
                npcsBySpecialShop ??= BuildSpecialShopNpcLookup();
                gatheringBasesByItem ??= BuildGatheringItemLookup();
                gatheringPointByBase ??= BuildGatheringPointLookup();
            }
        }
    }

    private ItemAcquisition? BuildAcquisition(uint itemRowId, uint gilPrice)
    {
        EnsureAcquisitionLookups();

        var recipes = new List<RecipeSource>();
        if (recipesByItem!.TryGetValue(itemRowId, out var recipeIds))
        {
            var sheet = Sheet<Recipe>();
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
        if (shopsByItem!.TryGetValue(itemRowId, out var shopIds))
        {
            var seen = new HashSet<(string Name, uint MapId, float X, float Y)>();
            foreach (var shopId in shopIds)
            {
                if (!npcsByShop!.TryGetValue(shopId, out var npcIds))
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

        return recipes.Count == 0 && vendors.Count == 0 && exchanges.Count == 0 && gathering.Count == 0
            ? null
            : new ItemAcquisition(recipes, vendors, exchanges, gathering);
    }

    private List<ExchangeSource> BuildExchanges(uint itemRowId)
    {
        var exchanges = new List<ExchangeSource>();
        if (!specialShopsByItem!.TryGetValue(itemRowId, out var shopIds))
            return exchanges;

        var seen = new HashSet<(string Shop, string Npc, uint MapId, float X, float Y)>();
        foreach (var shopId in shopIds)
        {
            var shop = Sheet<SpecialShop>().GetRowOrDefault(shopId);
            if (shop == null || !npcsBySpecialShop!.TryGetValue(shopId, out var npcIds))
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

    private static List<string> BuildExchangeCosts(SpecialShop shop, uint itemRowId)
    {
        // Each shop entry is one offer; its cost slots are all required together.
        // Tomestone stand-in slots resolve to no named item and are skipped.
        var costs = new List<string>();
        foreach (var entry in shop.Item)
        {
            if (!entry.ReceiveItems.Any(r => r.Item.RowId == itemRowId))
                continue;

            var parts = new List<string>();
            foreach (var cost in entry.ItemCosts)
            {
                if (cost.ItemCost.ValueNullable is { } costItem && !costItem.Name.IsEmpty)
                    parts.Add($"{cost.CurrencyCost}x {costItem.Name.ExtractText()}");
            }

            if (parts.Count > 0)
                costs.Add(string.Join(" + ", parts));
        }

        return costs;
    }

    private List<GatheringSource> BuildGathering(uint itemRowId)
    {
        var gathering = new List<GatheringSource>();
        if (!gatheringBasesByItem!.TryGetValue(itemRowId, out var baseIds))
            return gathering;

        var seen = new HashSet<(string Type, string Zone, float X, float Y)>();
        foreach (var baseId in baseIds)
        {
            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(baseId);
            if (pointBase == null)
                continue;

            var nodeType = pointBase.Value.GatheringType.ValueNullable?.Name.ExtractText() ?? "";
            var location = GatheringLocation(baseId);
            var key = location is { } loc
                ? (nodeType, loc.ZoneName, MathF.Round(loc.MapX, 1), MathF.Round(loc.MapY, 1))
                : (nodeType, "", 0f, 0f);
            if (seen.Add(key))
                gathering.Add(new GatheringSource(nodeType, pointBase.Value.GatheringLevel, location));
        }

        return gathering;
    }

    private MapLocation? GatheringLocation(uint baseRowId)
    {
        if (!gatheringPointByBase!.TryGetValue(baseRowId, out var pointId))
            return null;

        var point = Sheet<GatheringPoint>().GetRowOrDefault(pointId);
        var territory = point?.TerritoryType.ValueNullable;
        var map = territory?.Map.ValueNullable;
        var exported = Sheet<ExportedGatheringPoint>().GetRowOrDefault(baseRowId);
        if (territory == null || map == null || exported == null)
            return null;

        var zone = territory.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        var mapX = MapCoordConverter.ToMapCoord(exported.Value.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(exported.Value.Y, map.Value.SizeFactor, map.Value.OffsetY);
        return new MapLocation(territory.Value.RowId, map.Value.RowId, mapX, mapY, zone);
    }

    private Dictionary<uint, List<uint>> BuildRecipeLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var recipe in Sheet<Recipe>())
        {
            if (recipe.ItemResult.RowId != 0)
                Add(lookup, recipe.ItemResult.RowId, recipe.RowId);
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildShopItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var group in SubrowSheet<GilShopItem>())
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
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x40000 and < 0x50000)
                    Add(lookup, handler.RowId, npcBase.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildSpecialShopItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var shop in Sheet<SpecialShop>())
        {
            foreach (var entry in shop.Item)
            {
                foreach (var receive in entry.ReceiveItems)
                {
                    if (receive.Item.RowId != 0)
                        Add(lookup, receive.Item.RowId, shop.RowId);
                }
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildSpecialShopNpcLookup()
    {
        // Special shop event ids live in the 0x1B0000 block of ENpcData.
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x1B0000 and < 0x1C0000)
                    Add(lookup, handler.RowId, npcBase.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> GatheringBases()
    {
        var lookup = gatheringBasesByItem;
        if (lookup == null)
        {
            lock (acquisitionLock)
            {
                lookup = gatheringBasesByItem ??= BuildGatheringItemLookup();
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildGatheringItemLookup()
    {
        var itemsByGatheringItem = new Dictionary<uint, uint>();
        foreach (var row in Sheet<GatheringItem>())
        {
            if (row.Item.RowId != 0)
                itemsByGatheringItem.TryAdd(row.RowId, row.Item.RowId);
        }

        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var pointBase in Sheet<GatheringPointBase>())
        {
            foreach (var entry in pointBase.Item)
            {
                if (itemsByGatheringItem.TryGetValue(entry.RowId, out var itemId))
                    Add(lookup, itemId, pointBase.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildGatheringPointLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var point in Sheet<GatheringPoint>())
        {
            if (point.GatheringPointBase.RowId != 0 && point.TerritoryType.RowId != 0 && point.PlaceName.RowId != 0)
                lookup.TryAdd(point.GatheringPointBase.RowId, point.RowId);
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
        var row = Sheet<ENpcResident>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        var (handlers, sceneQuests) = ReadEventHandlers(rowId);
        return new NpcEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), FindLocation(rowId), handlers)
        {
            SceneQuests = sceneQuests,
        };
    }

    private (int Count, IReadOnlyList<CutsceneAppearance> SceneQuests) ReadEventHandlers(uint npcRowId)
    {
        var row = Sheet<ENpcBase>().GetRowOrDefault(npcRowId);
        if (row == null)
            return (0, []);

        var count = 0;
        List<CutsceneAppearance>? quests = null;
        foreach (var handler in row.Value.ENpcData)
        {
            if (handler.RowId == 0)
                continue;
            count++;

            // Quest handler ids live in the 0x10000 block and double as Quest row ids.
            if (handler.RowId is < 0x10000 or >= 0x20000)
                continue;

            var quest = Sheet<Quest>().GetRowOrDefault(handler.RowId);
            if (quest == null || quest.Value.Name.IsEmpty)
                continue;

            (quests ??= []).Add(new CutsceneAppearance(
                new QuestLink(handler.RowId, quest.Value.Name.ExtractText()),
                quest.Value.Expansion.ValueNullable?.Name.ExtractText() ?? "",
                (int)quest.Value.Expansion.RowId));
        }

        return (count, quests ?? (IReadOnlyList<CutsceneAppearance>)[]);
    }

    public QuestEntity? GetQuest(uint rowId)
    {
        var quest = BuildQuest(rowId);
        if (quest is not { Prerequisites.Count: > 0 })
            return quest;

        var (chains, msqVersion) = QuestChainWalker.Walk(quest, BuildQuest);
        return quest with { UnlockChains = chains, MsqRequirement = msqVersion };
    }

    private QuestEntity? BuildQuest(uint rowId)
    {
        var row = Sheet<Quest>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var quest = row.Value;
        var prerequisites = new List<QuestLink>();
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && previous.ValueNullable is { } prev && !prev.Name.IsEmpty)
                prerequisites.Add(new QuestLink(previous.RowId, prev.Name.ExtractText()));
        }

        var genre = quest.JournalGenre.ValueNullable;
        var category = genre?.JournalCategory.ValueNullable?.Name.ExtractText() ?? "";

        return new QuestEntity(
            rowId,
            quest.Name.ExtractText(),
            quest.ClassJobLevel.FirstOrDefault(),
            genre?.Name.ExtractText() ?? "",
            prerequisites)
        {
            PrerequisiteJoin = quest.PreviousQuestJoin == 2 ? QuestJoin.Any
                : prerequisites.Count > 0 ? QuestJoin.All : QuestJoin.None,
            Expansion = $"{quest.Expansion.RowId + 2}.x",
            MainScenario = IsMainScenario(category),
            StartLocation = quest.IssuerLocation.ValueNullable is { } issuer ? ToMapLocation(issuer) : null,
        };
    }

    public MountEntity? GetMount(uint rowId)
    {
        var row = Sheet<Mount>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MountEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon);
    }

    public MinionEntity? GetMinion(uint rowId)
    {
        var row = Sheet<Companion>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MinionEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon);
    }

    public AchievementEntity? GetAchievement(uint rowId)
    {
        var row = Sheet<Achievement>().GetRowOrDefault(rowId);
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
        if (rowId >= CuratedZoneBase)
            return GetCuratedZone(rowId);

        var row = Sheet<ContentFinderCondition>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var duty = row.Value;
        var solo = duty.ContentType.RowId is ContentTypeQuestBattles or ContentTypeMaskedCarnivale;

        var unlockId = ResolveUnlockQuestId(duty);
        var unlockEntity = unlockId != 0 ? GetQuest(unlockId) : null;

        return new DutyEntity(
            rowId,
            duty.Name.ExtractText(),
            duty.ContentType.ValueNullable?.Name.ExtractText() ?? "",
            duty.ClassJobLevelRequired,
            duty.ItemLevelRequired,
            solo,
            duty.HighEndDuty,
            duty.TerritoryType.RowId)
        {
            UnlockQuest = unlockEntity != null ? new QuestLink(unlockId, unlockEntity.Name) : null,
            ChainStart = unlockEntity?.UnlockChains.FirstOrDefault(c => c.Steps.Count > 0)?.Steps[0].Quest,
            MsqGate = ResolveMsqGate(unlockId, unlockEntity),
            FieldArea = IsFieldArea(duty),
            Optional = unlockEntity is { MainScenario: false },
        };
    }

    // The gate nearest the unlock quest (last in play order) is the most
    // advanced main-scenario requirement; MSQ linearity makes testing only
    // that one sufficient.
    private static MsqGate? ResolveMsqGate(uint unlockId, QuestEntity? unlock) =>
        unlock == null ? null
        : unlock.MainScenario ? new MsqGate(new QuestLink(unlockId, unlock.Name), unlock.Expansion)
        : unlock.UnlockChains.Select(c => c.Gate).LastOrDefault(g => g != null);

    private DutyEntity? GetCuratedZone(uint rowId)
    {
        foreach (var zone in CuratedZones)
        {
            if (zone.RowId != rowId)
                continue;

            if (GetQuest(zone.QuestId) is not { } unlock)
                return null;

            return new DutyEntity(rowId, zone.Name, zone.Kind, 0, 0, false, false, 0)
            {
                UnlockQuest = new QuestLink(zone.QuestId, unlock.Name),
                ChainStart = unlock.UnlockChains.FirstOrDefault(c => c.Steps.Count > 0)?.Steps[0].Quest,
                MsqGate = ResolveMsqGate(zone.QuestId, unlock),
                FieldArea = true,
                Optional = !unlock.MainScenario,
            };
        }

        return null;
    }

    private static bool IsFieldArea(ContentFinderCondition row) =>
        row.ContentLinkType == ContentLinkTypePublicContent && AreaContentTypes.Contains(row.ContentType.RowId);

    public DutyEntity? FindDutyByTerritory(uint territoryTypeId) =>
        DutyTerritoryLookup().TryGetValue(territoryTypeId, out var rowId) ? GetDuty(rowId) : null;

    private Dictionary<uint, uint> DutyTerritoryLookup()
    {
        var lookup = dutyByTerritory;
        if (lookup == null)
        {
            lock (dutyLock)
            {
                lookup = dutyByTerritory ??= BuildDutyTerritoryLookup();
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildDutyTerritoryLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var duty in Sheet<ContentFinderCondition>())
        {
            if (duty.TerritoryType.RowId != 0 && !duty.Name.IsEmpty)
                lookup.TryAdd(duty.TerritoryType.RowId, duty.RowId);
        }

        return lookup;
    }

    private MapLocation? FindLocation(uint npcRowId) =>
        NpcLevels().TryGetValue(npcRowId, out var level) ? ToMapLocation(level) : null;

    private Dictionary<uint, Level> NpcLevels()
    {
        var levels = npcLevels;
        if (levels == null)
        {
            lock (levelLock)
            {
                levels = npcLevels ??= BuildNpcLevelLookup();
            }
        }

        return levels;
    }

    private static MapLocation? ToMapLocation(Level level)
    {
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
        foreach (var level in Sheet<Level>())
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
