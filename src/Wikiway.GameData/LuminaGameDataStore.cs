using Lumina.Excel;
using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;

namespace Wikiway.GameData;

public sealed class LuminaGameDataStore : IGameDataStore
{
    private const byte LevelObjectTypeEventNpc = 8;

    // MapMarker.DataType: 3 keys an Aetheryte row, 4 an aethernet shard's
    // PlaceName. Aetheryte.Level[] is empty for every teleport aetheryte, so
    // the map markers are the only sheet-side source of their coordinates
    // (probed 7.3: 107/107 named aetherytes marked on their own map).
    private const byte MapMarkerAetheryte = 3;
    private const byte MapMarkerAethernet = 4;

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
    private readonly Lock huntLock = new();
    private Dictionary<uint, string>? markZoneByBNpcName;
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
    private Dictionary<uint, List<uint>>? fishingSpotsByItem;
    private Dictionary<uint, List<uint>>? spearfishingSpotsByItem;
    private Dictionary<uint, uint>? fishNoteByItem;
    private Dictionary<uint, List<GcSealOffer>>? sealOffersByItem;
    private Dictionary<uint, uint>? quartermasterByCompany;
    private Dictionary<uint, List<uint>>? venturesByItem;
    private Dictionary<uint, uint>? ventureTaskByNormal;
    private ItemActionTeachers? teachers;
    private Dictionary<uint, int>? recipeUsesByItem;
    private Dictionary<uint, List<uint>>? deliveryNpcsByItem;
    private Dictionary<uint, List<CollectableTurnIn>>? collectableTurnInsByItem;
    private Dictionary<uint, List<uint>>? treasureRanksByItem;
    private Dictionary<uint, string>? materiaTagByItem;
    private readonly Lock aetheryteLock = new();
    private Dictionary<uint, List<AetheryteMarker>>? aetherytesByMap;

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
        ct.ThrowIfCancellationRequested();
        MarkZones();
        ct.ThrowIfCancellationRequested();
        AetheryteMarkers();
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
        foreach (var row in Sheet<Orchestrion>())
            AddName(names, EntityKind.Orchestrion, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<TripleTriadCard>())
            AddName(names, EntityKind.TripleTriadCard, row.RowId, row.Name.ExtractText());

        // Command-less emote rows are event-stage variants (duplicate Snowball
        // entries and the like), not something a player can look up.
        foreach (var row in Sheet<Emote>())
        {
            if (row.TextCommand.ValueNullable is { } command && !command.Command.IsEmpty)
                AddName(names, EntityKind.Emote, row.RowId, row.Name.ExtractText());
        }

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Adventure>())
            AddName(names, EntityKind.Vista, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<NotoriousMonster>())
        {
            if (row.BNpcName.ValueNullable is { } mark && !mark.Singular.IsEmpty)
                AddName(names, EntityKind.HuntMark, row.RowId, mark.Singular.ExtractText());
        }

        // Indexed under both word orders so "kholusia" and "aether currents"
        // both reach the zone card; the duplicates collapse at match time.
        foreach (var row in Sheet<AetherCurrentCompFlgSet>())
        {
            var zone = row.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (zone.Length > 0)
            {
                AddName(names, EntityKind.AetherCurrentZone, row.RowId, $"{zone} aether currents");
                AddName(names, EntityKind.AetherCurrentZone, row.RowId, $"aether currents {zone}");
            }
        }

        ct.ThrowIfCancellationRequested();
        foreach (var row in Sheet<Fate>())
            AddName(names, EntityKind.Fate, row.RowId, row.Name.ExtractText());

        foreach (var row in Sheet<Leve>())
            AddName(names, EntityKind.Leve, row.RowId, row.Name.ExtractText());

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
            BuildAcquisition(rowId, row.Value.PriceMid))
        {
            Equipment = BuildEquipment(row.Value),
            Usage = BuildUsage(rowId),
            Food = BuildFood(row.Value),
        };
    }

    // HQ bonuses live in BaseParamSpecial/BaseParamValueSpecial as deltas keyed
    // by BaseParam row id; the combat numbers use these fixed rows (probed
    // 2026-08-23 against 7.3 sheets: Bronze Cuirass, Weathered War Axe, Square
    // Maple Shield). No delay delta exists.
    private const uint ParamPhysicalDamage = 12;
    private const uint ParamMagicDamage = 13;
    private const uint ParamBlockRate = 17;
    private const uint ParamBlockStrength = 18;
    private const uint ParamDefense = 21;
    private const uint ParamMagicDefense = 24;

    private ItemEquipment? BuildEquipment(Item row)
    {
        var slots = row.EquipSlotCategory.ValueNullable;
        if (row.EquipSlotCategory.RowId == 0 || slots == null)
            return null;

        var hqDeltas = new Dictionary<uint, short>();
        if (row.CanBeHq)
            for (var i = 0; i < row.BaseParamSpecial.Count && i < row.BaseParamValueSpecial.Count; i++)
                if (row.BaseParamSpecial[i].RowId != 0 && row.BaseParamValueSpecial[i] != 0)
                    hqDeltas[row.BaseParamSpecial[i].RowId] = row.BaseParamValueSpecial[i];

        var stats = new List<EquipStat>();
        for (var i = 0; i < row.BaseParam.Count && i < row.BaseParamValue.Count; i++)
        {
            var paramId = row.BaseParam[i].RowId;
            if (paramId == 0)
                continue;
            var hqBonus = hqDeltas.GetValueOrDefault(paramId);
            if (row.BaseParamValue[i] == 0 && hqBonus == 0)
                continue;
            var name = row.BaseParam[i].ValueNullable?.Name.ExtractText() ?? "";
            if (name.Length > 0)
                stats.Add(new EquipStat(name, row.BaseParamValue[i], hqBonus));
        }

        var repairJob = row.ClassJobRepair.ValueNullable?.Abbreviation.ExtractText() ?? "";
        var repairMat = row.ItemRepair.ValueNullable?.Item.ValueNullable?.Name.ExtractText() ?? "";
        var special = row.ItemSpecialBonus.ValueNullable?.Name.ExtractText() ?? "";
        if (special.Length > 0 && row.ItemSpecialBonusParam > 0)
            special = $"{special} ({row.ItemSpecialBonusParam})";

        return new ItemEquipment(
            SlotName(slots.Value),
            (ushort)row.LevelItem.RowId,
            row.LevelEquip,
            row.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "",
            stats)
        {
            Weapon = row.DamagePhys > 0 || row.DamageMag > 0
                ? new WeaponInfo(row.DamagePhys, row.DamageMag, row.Delayms / 1000.0,
                    (ushort)hqDeltas.GetValueOrDefault(ParamPhysicalDamage),
                    (ushort)hqDeltas.GetValueOrDefault(ParamMagicDamage))
                : null,
            Defense = row.DefensePhys > 0 || row.DefenseMag > 0
                ? new DefenseInfo(row.DefensePhys, row.DefenseMag,
                    (ushort)hqDeltas.GetValueOrDefault(ParamDefense),
                    (ushort)hqDeltas.GetValueOrDefault(ParamMagicDefense))
                : null,
            Block = row.Block > 0 || row.BlockRate > 0
                ? new BlockInfo(row.Block, row.BlockRate,
                    (ushort)hqDeltas.GetValueOrDefault(ParamBlockStrength),
                    (ushort)hqDeltas.GetValueOrDefault(ParamBlockRate))
                : null,
            MateriaSlots = row.MateriaSlotCount,
            AdvancedMelding = row.IsAdvancedMeldingPermitted,
            Unique = row.IsUnique,
            Untradable = row.IsUntradable,
            CanBeHq = row.CanBeHq,
            DyeCount = row.DyeCount,
            Repair = repairJob.Length > 0 && repairMat.Length > 0 ? $"{repairJob} · {repairMat}" : "",
            Series = row.ItemSeries.ValueNullable?.Name.ExtractText() ?? "",
            SpecialBonus = special,
            Desynthable = row.Desynth > 0,
            SellPrice = row.PriceLow,
        };
    }

    // 1 marks the slot the item occupies (-1 marks slots it blocks, e.g. a
    // two-hander's off hand); rings carry 1 in both finger columns.
    private static string SlotName(EquipSlotCategory s) => s switch
    {
        { MainHand: 1 } => "Main Hand",
        { OffHand: 1 } => "Off Hand",
        { Head: 1 } => "Head",
        { Body: 1 } => "Body",
        { Gloves: 1 } => "Hands",
        { Legs: 1 } => "Legs",
        { Feet: 1 } => "Feet",
        { Ears: 1 } => "Ears",
        { Neck: 1 } => "Neck",
        { Wrists: 1 } => "Wrists",
        { FingerL: 1 } or { FingerR: 1 } => "Ring",
        { Waist: 1 } => "Waist",
        { SoulCrystal: 1 } => "Soul Crystal",
        _ => "",
    };

    private void EnsureAcquisitionLookups()
    {
        if (recipesByItem == null || shopsByItem == null || npcsByShop == null ||
            specialShopsByItem == null || npcsBySpecialShop == null ||
            gatheringBasesByItem == null || gatheringPointByBase == null ||
            fishingSpotsByItem == null || spearfishingSpotsByItem == null ||
            fishNoteByItem == null || sealOffersByItem == null ||
            quartermasterByCompany == null || venturesByItem == null ||
            ventureTaskByNormal == null || teachers == null ||
            recipeUsesByItem == null || deliveryNpcsByItem == null ||
            collectableTurnInsByItem == null || treasureRanksByItem == null ||
            materiaTagByItem == null)
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
                fishingSpotsByItem ??= BuildFishingSpotLookup();
                spearfishingSpotsByItem ??= BuildSpearfishingLookup();
                fishNoteByItem ??= BuildFishNoteLookup();
                sealOffersByItem ??= BuildSealOfferLookup();
                quartermasterByCompany ??= BuildQuartermasterLookup();
                venturesByItem ??= BuildVentureItemLookup();
                ventureTaskByNormal ??= BuildVentureTaskLookup();
                teachers ??= BuildTeacherLookup();
                recipeUsesByItem ??= BuildRecipeUseLookup();
                deliveryNpcsByItem ??= BuildDeliveryLookup();
                collectableTurnInsByItem ??= BuildCollectableLookup();
                treasureRanksByItem ??= BuildTreasureRankLookup();
                materiaTagByItem ??= BuildMateriaTagLookup();
            }
        }
    }

    // ItemAction type discriminators (ItemAction.Action.RowId); Data[0] names
    // the taught row, except orchestrion rolls where Item.AdditionalData does
    // and emote manuals where Data[0] is the Emote.UnlockLink unlock bit.
    // Probed 2026-09-01 against 7.3 sheets (Aithon Whistle, Wind-up Cursor,
    // A Cold Wind Orchestrion Roll, Momodi Modi Card, The Bomb Dance).
    private const uint ItemActionMount = 1322;
    private const uint ItemActionMinion = 853;
    private const uint ItemActionOrchestrion = 25183;
    private const uint ItemActionTripleTriadCard = 3357;
    private const uint ItemActionUnlockBit = 2633;

    private sealed record ItemActionTeachers(
        Dictionary<uint, uint> ByMount,
        Dictionary<uint, uint> ByMinion,
        Dictionary<uint, uint> ByOrchestrion,
        Dictionary<uint, uint> ByCard,
        Dictionary<uint, uint> ByUnlockBit);

    private ItemActionTeachers Teachers()
    {
        EnsureAcquisitionLookups();
        return teachers!;
    }

    private ItemActionTeachers BuildTeacherLookup()
    {
        var lookup = new ItemActionTeachers([], [], [], [], []);
        foreach (var item in Sheet<Item>())
        {
            if (item.Name.IsEmpty || item.ItemAction.ValueNullable is not { } action)
                continue;

            switch (action.Action.RowId)
            {
                case ItemActionMount:
                    lookup.ByMount.TryAdd(action.Data[0], item.RowId);
                    break;
                case ItemActionMinion:
                    lookup.ByMinion.TryAdd(action.Data[0], item.RowId);
                    break;
                case ItemActionOrchestrion:
                    lookup.ByOrchestrion.TryAdd(item.AdditionalData.RowId, item.RowId);
                    break;
                case ItemActionTripleTriadCard:
                    lookup.ByCard.TryAdd(action.Data[0], item.RowId);
                    break;
                case ItemActionUnlockBit:
                    lookup.ByUnlockBit.TryAdd(action.Data[0], item.RowId);
                    break;
            }
        }

        return lookup;
    }

    private ItemEntity? TeachingItem(Dictionary<uint, uint> lookup, uint rowId) =>
        lookup.TryGetValue(rowId, out var itemId) ? GetItem(itemId) : null;

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
        var fishing = BuildFishing(itemRowId);
        var sealVendors = BuildSealVendors(itemRowId);
        var ventures = BuildVentures(itemRowId);
        var fishingNote = fishing.Count > 0 ? FishNote(itemRowId) : "";

        return recipes.Count == 0 && vendors.Count == 0 && exchanges.Count == 0 && gathering.Count == 0 &&
               fishing.Count == 0 && sealVendors.Count == 0 && ventures.Count == 0
            ? null
            : new ItemAcquisition(recipes, vendors, exchanges, gathering, fishing, sealVendors, ventures, fishingNote);
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

    private List<GatheringSource> BuildGathering(uint itemRowId)
    {
        var gathering = new List<GatheringSource>();
        if (!gatheringBasesByItem!.TryGetValue(itemRowId, out var baseIds))
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

    // Sheet times are Eorzean clock values (900 = 9:00). Durations count the
    // same way but carry past 60 minutes (300 = 3h ARR windows, 160 = 2h HW
    // windows) - probe-verified against Spruce Log 9:00-12:00 and Chysahl
    // Greens 8:00-10:00/20:00-22:00; 65535 marks an unused slot.
    private const ushort UnusedTime = 65535;

    private static int ToEorzeaMinutes(ushort clock) => ((clock / 100) * 60) + (clock % 100);

    private static string ClockLabel(int minutes)
    {
        if (minutes != 24 * 60)
            minutes %= 24 * 60;
        return $"{minutes / 60}:{minutes % 60:00}";
    }

    private string GatheringTimeWindow(uint baseRowId)
    {
        if (!gatheringPointByBase!.TryGetValue(baseRowId, out var pointId))
            return "";

        var transient = Sheet<GatheringPointTransient>().GetRowOrDefault(pointId);
        if (transient == null)
            return "";

        if (transient.Value.GatheringRarePopTimeTable.ValueNullable is { } table)
        {
            var windows = new List<string>();
            for (var i = 0; i < table.StartTime.Count && i < table.Duration.Count; i++)
            {
                if (table.StartTime[i] == UnusedTime)
                    continue;

                var start = ToEorzeaMinutes(table.StartTime[i]);
                windows.Add($"{ClockLabel(start)}-{ClockLabel(start + ToEorzeaMinutes(table.Duration[i]))}");
            }

            if (windows.Count > 0)
                return $"Unspoiled · {string.Join(", ", windows)} ET";
        }

        var ephemeralStart = transient.Value.EphemeralStartTime;
        var ephemeralEnd = transient.Value.EphemeralEndTime;
        if (ephemeralStart == UnusedTime || ephemeralStart == ephemeralEnd)
            return "";

        var end = ephemeralEnd == 0 ? 24 * 60 : ToEorzeaMinutes(ephemeralEnd);
        return $"Ephemeral · {ClockLabel(ToEorzeaMinutes(ephemeralStart))}-{ClockLabel(end)} ET";
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

        var mapX = MapCoordConverter.ToMapCoord(exported.Value.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(exported.Value.Y, map.Value.SizeFactor, map.Value.OffsetY);
        return Locate(territory.Value, map.Value, mapX, mapY);
    }

    private List<FishingSource> BuildFishing(uint itemRowId)
    {
        var fishing = new List<FishingSource>();
        if (fishingSpotsByItem!.TryGetValue(itemRowId, out var spotIds))
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

        if (spearfishingSpotsByItem!.TryGetValue(itemRowId, out var notebookIds))
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

    private string FishNote(uint itemRowId)
    {
        if (!fishNoteByItem!.TryGetValue(itemRowId, out var paramId))
            return "";

        var row = Sheet<FishParameter>().GetRowOrDefault(paramId);
        return row?.Text.ExtractText() ?? "";
    }

    private List<SealVendorSource> BuildSealVendors(uint itemRowId)
    {
        var vendors = new List<SealVendorSource>();
        if (!sealOffersByItem!.TryGetValue(itemRowId, out var offers))
            return vendors;

        var seen = new HashSet<GcSealOffer>();
        foreach (var offer in offers)
        {
            if (!seen.Add(offer) || !quartermasterByCompany!.TryGetValue(offer.GrandCompany, out var npcId))
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

    private List<VentureSource> BuildVentures(uint itemRowId)
    {
        var ventures = new List<VentureSource>();
        if (!venturesByItem!.TryGetValue(itemRowId, out var normalIds))
            return ventures;

        foreach (var normalId in normalIds)
        {
            if (!ventureTaskByNormal!.TryGetValue(normalId, out var taskId))
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

    // A direct 0x1B0000 handler reaches only ~31% of offer-carrying shops
    // (probed 2026-09-01: 375 of 1215). The rest hang off menu and aggregator
    // handlers - TopicSelect pages, InclusionShop category trees, PreHandler
    // gates, CustomTalk scripts - and bicolor-gemstone FateShops are keyed by
    // the vendor NPC row itself. Shops no mechanism reaches are dev leftovers
    // and superseded content; leaving them unmapped is the noise filter.
    private Dictionary<uint, List<uint>> BuildSpecialShopNpcLookup()
    {
        var shops = new HashSet<uint>();
        foreach (var shop in Sheet<SpecialShop>())
            shops.Add(shop.RowId);

        var preTargets = new Dictionary<uint, uint>();
        foreach (var pre in Sheet<PreHandler>())
        {
            if (pre.Target.RowId != 0)
                preTargets.TryAdd(pre.RowId, pre.Target.RowId);
        }

        var topicEntries = new Dictionary<uint, List<uint>>();
        foreach (var topic in Sheet<TopicSelect>())
        {
            foreach (var entry in topic.Shop)
            {
                if (entry.RowId != 0)
                    Add(topicEntries, topic.RowId, entry.RowId);
            }
        }

        var inclusionShops = new Dictionary<uint, List<uint>>();
        foreach (var inclusion in Sheet<InclusionShop>())
        {
            foreach (var category in inclusion.Category)
            {
                if (category.ValueNullable?.InclusionShopSeries.ValueNullable is not { } series)
                    continue;

                foreach (var sub in series)
                {
                    if (sub.SpecialShop.RowId != 0)
                        Add(inclusionShops, inclusion.RowId, sub.SpecialShop.RowId);
                }
            }
        }

        var customTalkShops = new Dictionary<uint, List<uint>>();
        foreach (var talk in Sheet<CustomTalk>())
        {
            foreach (var script in talk.Script)
            {
                if (shops.Contains(script.ScriptArg))
                    Add(customTalkShops, talk.RowId, script.ScriptArg);
            }
        }

        foreach (var group in SubrowSheet<CustomTalkNestHandlers>())
        {
            foreach (var sub in group)
            {
                if (shops.Contains(sub.NestHandler.RowId))
                    Add(customTalkShops, group.RowId, sub.NestHandler.RowId);
            }
        }

        var lookup = new Dictionary<uint, List<uint>>();

        // Deepest observed chain is TopicSelect -> PreHandler -> shop; the
        // budget only exists to stop a malformed self-referencing handler.
        void Resolve(uint handlerId, uint npcId, int depth)
        {
            if (shops.Contains(handlerId))
            {
                Add(lookup, handlerId, npcId);
                return;
            }

            if (depth == 0)
                return;

            if (preTargets.TryGetValue(handlerId, out var target))
                Resolve(target, npcId, depth - 1);

            if (topicEntries.TryGetValue(handlerId, out var entries))
            {
                foreach (var entry in entries)
                    Resolve(entry, npcId, depth - 1);
            }

            if (inclusionShops.TryGetValue(handlerId, out var included))
            {
                foreach (var shopId in included)
                    Add(lookup, shopId, npcId);
            }

            if (customTalkShops.TryGetValue(handlerId, out var scripted))
            {
                foreach (var shopId in scripted)
                    Add(lookup, shopId, npcId);
            }
        }

        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId != 0)
                    Resolve(handler.RowId, npcBase.RowId, 3);
            }
        }

        foreach (var fateShop in Sheet<FateShop>())
        {
            if (fateShop.RowId == 0)
                continue;

            foreach (var shopRef in fateShop.SpecialShop)
            {
                if (shopRef.RowId != 0)
                    Add(lookup, shopRef.RowId, fateShop.RowId);
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

    private Dictionary<uint, List<uint>> BuildFishingSpotLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var spot in Sheet<FishingSpot>())
        {
            if (spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0)
                continue;

            foreach (var item in spot.Item)
            {
                if (item.RowId != 0)
                    Add(lookup, item.RowId, spot.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildSpearfishingLookup()
    {
        // Spearfishing bases fill their GatheringPointBase.Item slots with
        // SpearfishingItem row ids (20000+), disjoint from GatheringItem ids,
        // so the regular gathering index never sees them.
        var itemBySpearRow = new Dictionary<uint, uint>();
        foreach (var row in Sheet<SpearfishingItem>())
        {
            if (row.Item.RowId != 0)
                itemBySpearRow.TryAdd(row.RowId, row.Item.RowId);
        }

        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var spot in Sheet<SpearfishingNotebook>())
        {
            var pointBase = Sheet<GatheringPointBase>().GetRowOrDefault(spot.GatheringPointBase.RowId);
            if (pointBase == null || spot.TerritoryType.RowId == 0 || spot.PlaceName.RowId == 0)
                continue;

            foreach (var entry in pointBase.Value.Item)
            {
                if (itemBySpearRow.TryGetValue(entry.RowId, out var itemId))
                    Add(lookup, itemId, spot.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildFishNoteLookup()
    {
        var lookup = new Dictionary<uint, uint>();
        foreach (var row in Sheet<FishParameter>())
        {
            if (row.Item.RowId != 0 && !row.Text.IsEmpty)
                lookup.TryAdd(row.Item.RowId, row.RowId);
        }

        return lookup;
    }

    private sealed record GcSealOffer(uint GrandCompany, uint Seals, uint Rank);

    private Dictionary<uint, List<GcSealOffer>> BuildSealOfferLookup()
    {
        var lookup = new Dictionary<uint, List<GcSealOffer>>();
        var categories = Sheet<GCScripShopCategory>();
        foreach (var group in SubrowSheet<GCScripShopItem>())
        {
            var company = categories.GetRowOrDefault(group.RowId)?.GrandCompany.RowId ?? 0;
            if (company == 0)
                continue;

            foreach (var sub in group)
            {
                if (sub.Item.RowId == 0)
                    continue;

                if (!lookup.TryGetValue(sub.Item.RowId, out var list))
                    lookup[sub.Item.RowId] = list = [];
                list.Add(new GcSealOffer(company, sub.CostGCSeals, sub.RequiredGrandCompanyRank.RowId));
            }
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildQuartermasterLookup()
    {
        // GC shop event ids live in the 0x160000 block of ENpcData; each
        // company has exactly one quartermaster NPC.
        var companyByShop = new Dictionary<uint, uint>();
        foreach (var shop in Sheet<GCShop>())
        {
            if (shop.GrandCompany.RowId != 0)
                companyByShop.TryAdd(shop.RowId, shop.GrandCompany.RowId);
        }

        var lookup = new Dictionary<uint, uint>();
        foreach (var npcBase in Sheet<ENpcBase>())
        {
            foreach (var handler in npcBase.ENpcData)
            {
                if (handler.RowId is >= 0x160000 and < 0x170000 &&
                    companyByShop.TryGetValue(handler.RowId, out var company))
                    lookup.TryAdd(company, npcBase.RowId);
            }
        }

        return lookup;
    }

    private Dictionary<uint, List<uint>> BuildVentureItemLookup()
    {
        var lookup = new Dictionary<uint, List<uint>>();
        foreach (var normal in Sheet<RetainerTaskNormal>())
        {
            if (normal.Item.RowId != 0)
                Add(lookup, normal.Item.RowId, normal.RowId);
        }

        return lookup;
    }

    private Dictionary<uint, uint> BuildVentureTaskLookup()
    {
        // RetainerTask.Task points at RetainerTaskNormal (or RetainerTaskRandom
        // for the exploration ventures, which never award a specific item).
        var lookup = new Dictionary<uint, uint>();
        foreach (var task in Sheet<RetainerTask>())
        {
            if (task.IsRandom || task.Task.RowId == 0)
                continue;

            if (!lookup.TryGetValue(task.Task.RowId, out var existing) || task.RowId < existing)
                lookup[task.Task.RowId] = task.RowId;
        }

        return lookup;
    }

    private static void Add(Dictionary<uint, List<uint>> lookup, uint key, uint value)
    {
        if (!lookup.TryGetValue(key, out var list))
            lookup[key] = list = [];
        list.Add(value);
    }

    private ItemUsage? BuildUsage(uint itemRowId)
    {
        EnsureAcquisitionLookups();

        var recipeUses = recipeUsesByItem!.GetValueOrDefault(itemRowId);
        var deliveries = BuildDeliveries(itemRowId);
        var turnIns = collectableTurnInsByItem!.GetValueOrDefault(itemRowId)
            ?? (IReadOnlyList<CollectableTurnIn>)[];
        var treasureMap = BuildTreasureMap(itemRowId);
        var materiaTag = materiaTagByItem!.GetValueOrDefault(itemRowId, "");

        return recipeUses == 0 && deliveries.Count == 0 && turnIns.Count == 0 &&
               treasureMap == null && materiaTag.Length == 0
            ? null
            : new ItemUsage(recipeUses, deliveries, turnIns, treasureMap, materiaTag);
    }

    private List<DeliverySource> BuildDeliveries(uint itemRowId)
    {
        var deliveries = new List<DeliverySource>();
        if (!deliveryNpcsByItem!.TryGetValue(itemRowId, out var npcIds))
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

    private TreasureMapInfo? BuildTreasureMap(uint itemRowId)
    {
        if (!treasureRanksByItem!.TryGetValue(itemRowId, out var rankIds))
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

    // Food and medicine actions carry (status, ItemFood row, duration in
    // seconds) in Data[0..2]: both food types buff Well Fed for 1800s, the
    // stat tinctures Medicated for 15-30s (probed 2026-09-01: Boiled Egg,
    // Rroneek Steak, Tincture of Strength). NQ and HQ share the ItemFood row;
    // HQ values live in its ValueHQ/MaxHQ columns. EXPBonusPercent is set on
    // every ItemFood row, but only Well Fed grants it in-game.
    private const uint ItemActionBattleFood = 844;
    private const uint ItemActionCraftFood = 845;
    private const uint ItemActionMedicine = 846;
    private const uint StatusWellFed = 48;

    private ItemFoodEffect? BuildFood(Item row)
    {
        if (row.ItemAction.ValueNullable is not { } action ||
            action.Action.RowId is not (ItemActionBattleFood or ItemActionCraftFood or ItemActionMedicine))
            return null;

        var food = Sheet<ItemFood>().GetRowOrDefault(action.Data[1]);
        if (food == null)
            return null;

        var stats = new List<FoodStat>();
        foreach (var param in food.Value.Params)
        {
            if (param.BaseParam.RowId == 0 ||
                param.BaseParam.ValueNullable is not { } baseParam || baseParam.Name.IsEmpty)
                continue;

            stats.Add(new FoodStat(
                baseParam.Name.ExtractText(),
                param.IsRelative,
                param.Value,
                param.Max,
                row.CanBeHq ? param.ValueHQ : param.Value,
                row.CanBeHq ? param.MaxHQ : param.Max));
        }

        return new ItemFoodEffect(
            Sheet<Status>().GetRowOrDefault(action.Data[0])?.Name.ExtractText() ?? "",
            action.Data[2],
            action.Data[0] == StatusWellFed ? food.Value.EXPBonusPercent : 0,
            stats);
    }

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

        // DescriptionEnhanced is the mount-guide lore text; plain Description
        // is the summon-action blurb and Tooltip a one-line joke.
        var transient = Sheet<MountTransient>().GetRowOrDefault(rowId);
        return new MountEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon)
        {
            Description = transient?.DescriptionEnhanced.ExtractText() ?? "",
            TeachingItem = TeachingItem(Teachers().ByMount, rowId),
        };
    }

    public MinionEntity? GetMinion(uint rowId)
    {
        var row = Sheet<Companion>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        var transient = Sheet<CompanionTransient>().GetRowOrDefault(rowId);
        return new MinionEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), row.Value.Icon)
        {
            Description = transient?.DescriptionEnhanced.ExtractText() ?? "",
            BattleStats = row.Value.HP > 0 && transient != null
                ? new MinionBattleStats(
                    row.Value.HP,
                    transient.Value.Attack,
                    transient.Value.Defense,
                    transient.Value.Speed,
                    row.Value.Cost,
                    transient.Value.SpecialActionName.ExtractText())
                : null,
            TeachingItem = TeachingItem(Teachers().ByMinion, rowId),
        };
    }

    public OrchestrionEntity? GetOrchestrion(uint rowId)
    {
        var row = Sheet<Orchestrion>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var ui = Sheet<OrchestrionUiparam>().GetRowOrDefault(rowId);
        return new OrchestrionEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            ui?.OrchestrionCategory.ValueNullable?.Name.ExtractText() ?? "")
        {
            TeachingItem = TeachingItem(Teachers().ByOrchestrion, rowId),
        };
    }

    // Acquisition column meaning depends on the obtain type (probed 2026-09-01
    // across all 476 cards): NPC-win types carry an ENpcResident id plus a
    // Level row, duty-drop types a ContentFinderCondition id. Other types hold
    // ids from unrelated sheets, so they must not be resolved.
    private static readonly uint[] CardObtainNpcTypes = [6, 10];
    private static readonly uint[] CardObtainDutyTypes = [2, 3];

    public TripleTriadCardEntity? GetTripleTriadCard(uint rowId)
    {
        var row = Sheet<TripleTriadCard>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var resident = Sheet<TripleTriadCardResident>().GetRowOrDefault(rowId);
        if (resident == null)
            return null;

        var r = resident.Value;
        var obtainType = r.AcquisitionType.RowId;

        var npcName = "";
        MapLocation? npcLocation = null;
        if (CardObtainNpcTypes.Contains(obtainType) &&
            Sheet<ENpcResident>().GetRowOrDefault(r.Acquisition.RowId) is { } npc && !npc.Singular.IsEmpty)
        {
            npcName = TitleCase(npc.Singular.ExtractText());
            npcLocation = Sheet<Level>().GetRowOrDefault(r.Location.RowId) is { } level
                ? ToMapLocation(level)
                : FindLocation(r.Acquisition.RowId);
        }

        var dutyName = "";
        if (CardObtainDutyTypes.Contains(obtainType) &&
            Sheet<ContentFinderCondition>().GetRowOrDefault(r.Acquisition.RowId) is { } duty && !duty.Name.IsEmpty)
            dutyName = duty.Name.ExtractText();

        return new TripleTriadCardEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            r.Top,
            r.Bottom,
            r.Left,
            r.Right,
            r.TripleTriadCardRarity.ValueNullable?.Stars ?? 0,
            r.TripleTriadCardType.ValueNullable?.Name.ExtractText() ?? "",
            r.SaleValue)
        {
            ObtainText = CleanObtainText(r.AcquisitionType.ValueNullable?.Text.ValueNullable?.Text.ExtractText() ?? ""),
            NpcName = npcName,
            NpcLocation = npcLocation,
            DutyName = dutyName,
            TeachingItem = TeachingItem(Teachers().ByCard, rowId),
        };
    }

    // Obtain labels are Addon strings with their payloads stripped, so several
    // come out as bare whitespace or end in a dangling colon.
    private static string CleanObtainText(string text)
    {
        var cleaned = text.Trim().TrimEnd(':', '.');
        return cleaned.Length > 1 ? cleaned : "";
    }

    private const uint QuestRowBlockStart = 0x10000;
    private const uint QuestRowBlockEnd = 0x20000;

    public EmoteEntity? GetEmote(uint rowId)
    {
        var row = Sheet<Emote>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // UnlockLink is a quest row only in the quest id block; other values
        // are unlock bits that a manual item's ItemAction may set. Bits taught
        // neither way (old achievement-era emotes) stay blank for the wiki.
        var link = row.Value.UnlockLink;
        QuestLink? unlockQuest = null;
        if (link is >= QuestRowBlockStart and < QuestRowBlockEnd &&
            Sheet<Quest>().GetRowOrDefault(link) is { } quest && !quest.Name.IsEmpty)
            unlockQuest = new QuestLink(link, quest.Name.ExtractText().Trim());

        return new EmoteEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.TextCommand.ValueNullable?.Command.ExtractText() ?? "",
            row.Value.EmoteCategory.ValueNullable?.Name.ExtractText() ?? "")
        {
            UnlockQuest = unlockQuest,
            TeachingItem = link == 0 ? null : TeachingItem(Teachers().ByUnlockBit, link),
        };
    }

    public VistaEntity? GetVista(uint rowId)
    {
        var row = Sheet<Adventure>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // Impression is the log's riddle hint; Description the location lore
        // (probed 2026-09-01: Barracuda Piers, Seasong Grotto).
        var emote = row.Value.Emote.ValueNullable;
        var command = emote?.TextCommand.ValueNullable?.Command.ExtractText() ?? "";
        return new VistaEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Impression.ExtractText(),
            row.Value.Description.ExtractText())
        {
            Location = row.Value.Level.ValueNullable is { } level ? ToMapLocation(level) : null,
            Region = row.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "",
            Emote = command.Length > 0 ? command : emote?.Name.ExtractText() ?? "",
            TimeWindow = VistaTimeWindow(row.Value.MinTime, row.Value.MaxTime),
        };
    }

    // Vista windows are ET clock values with an inclusive end (800-1159 is the
    // 8:00-12:00 window); 0-0 marks the always-available vistas.
    private static string VistaTimeWindow(ushort minTime, ushort maxTime)
    {
        if (minTime == 0 && maxTime == 0)
            return "";

        var end = (ToEorzeaMinutes(maxTime) + 1) % (24 * 60);
        return $"{ClockLabel(ToEorzeaMinutes(minTime))}-{ClockLabel(end == 0 ? 24 * 60 : end)} ET";
    }

    public HuntMarkEntity? GetHuntMark(uint rowId)
    {
        var row = Sheet<NotoriousMonster>().GetRowOrDefault(rowId);
        if (row == null || row.Value.BNpcName.ValueNullable is not { } mark || mark.Singular.IsEmpty)
            return null;

        return new HuntMarkEntity(rowId, TitleCase(mark.Singular.ExtractText()), RankLetter(row.Value.Rank))
        {
            ZoneName = MarkZones().GetValueOrDefault(row.Value.BNpcName.RowId, ""),
        };
    }

    private static string RankLetter(byte rank) => rank switch
    {
        1 => "B",
        2 => "A",
        3 => "S",
        _ => "",
    };

    private Dictionary<uint, string> MarkZones()
    {
        var lookup = markZoneByBNpcName;
        if (lookup == null)
        {
            lock (huntLock)
            {
                lookup = markZoneByBNpcName ??= BuildMarkZoneLookup();
            }
        }

        return lookup;
    }

    private Dictionary<uint, string> BuildMarkZoneLookup()
    {
        // MobHuntTarget is the hunt-bill sheet, so only billed marks (the B
        // ranks) appear; their PlaceName column holds the log sub-zone and is
        // empty for marks, so the zone comes from the Map row instead.
        var lookup = new Dictionary<uint, string>();
        foreach (var target in Sheet<MobHuntTarget>())
        {
            if (target.Name.RowId == 0)
                continue;

            var zone = target.Map.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
            if (zone.Length > 0)
                lookup.TryAdd(target.Name.RowId, zone);
        }

        return lookup;
    }

    public AetherCurrentZoneEntity? GetAetherCurrentZone(uint rowId)
    {
        var row = Sheet<AetherCurrentCompFlgSet>().GetRowOrDefault(rowId);
        var zone = row?.Territory.ValueNullable?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        if (row == null || zone.Length == 0)
            return null;

        var quests = new List<QuestChainStep>();
        foreach (var current in row.Value.AetherCurrents)
        {
            if (current.ValueNullable?.Quest is not { RowId: not 0 } questRef ||
                questRef.ValueNullable is not { } quest || quest.Name.IsEmpty)
                continue;

            quests.Add(new QuestChainStep(
                new QuestLink(questRef.RowId, quest.Name.ExtractText()),
                quest.ClassJobLevel.FirstOrDefault(),
                quest.IssuerLocation.ValueNullable is { } issuer ? ToMapLocation(issuer) : null));
        }

        return new AetherCurrentZoneEntity(rowId, zone, quests);
    }

    public FateEntity? GetFate(uint rowId)
    {
        var row = Sheet<Fate>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        // Fate.Location is an LGB instance id, not a Level row (probed
        // 2026-09-01: 0 of 1697 resolve), so FATE cards carry no map pin.
        var required = row.Value.RequiredQuest.ValueNullable;
        return new FateEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.ClassJobLevel,
            row.Value.Description.ExtractText())
        {
            RequiredQuest = required is { } quest && !quest.Name.IsEmpty
                ? new QuestLink(row.Value.RequiredQuest.RowId, quest.Name.ExtractText())
                : null,
        };
    }

    public LeveEntity? GetLeve(uint rowId)
    {
        var row = Sheet<Leve>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var leve = row.Value;
        return new LeveEntity(
            rowId,
            leve.Name.ExtractText(),
            leve.ClassJobLevel,
            leve.LeveAssignmentType.ValueNullable?.Name.ExtractText() ?? "",
            leve.ClassJobCategory.ValueNullable?.Name.ExtractText() ?? "",
            leve.Description.ExtractText())
        {
            Levemete = leve.LevelLevemete.ValueNullable is { } level ? ToMapLocation(level) : null,
            IssuedAt = leve.PlaceNameIssued.ValueNullable?.Name.ExtractText() ?? "",
            AllowanceCost = leve.AllowanceCost,
            ExpReward = leve.ExpReward,
            GilReward = leve.GilReward,
        };
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

    private MapLocation? ToMapLocation(Level level)
    {
        var map = level.Map.ValueNullable;
        var territory = level.Territory.ValueNullable;
        if (map == null || territory == null)
            return null;

        // World Z is the north-south axis; map Y comes from it, not from world Y (height).
        var mapX = MapCoordConverter.ToMapCoord(level.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(level.Z, map.Value.SizeFactor, map.Value.OffsetY);

        return Locate(territory.Value, map.Value, mapX, mapY);
    }

    private MapLocation Locate(TerritoryType territory, Map map, float mapX, float mapY)
    {
        var zone = territory.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        var territoryId = PublicTerritory(territory, map);
        return new MapLocation(territoryId, map.RowId, mapX, mapY, zone, NearestAetheryte(territoryId, map.RowId, mapX, mapY));
    }

    // Quest scenes place NPCs in private copies of a zone: separate
    // TerritoryType rows that share the public map and its place name (probed
    // 7.3: 405 of 446 mismatched NPC placements). A flag carrying the copy's
    // id never matches the territory the player stands in, so it is swapped
    // for the map's own territory when the names agree; sub-areas with their
    // own name (the Sanctum of the Twelve inside East Shroud) keep theirs.
    private static uint PublicTerritory(TerritoryType territory, Map map)
    {
        var owner = map.TerritoryType.ValueNullable;
        if (owner == null || owner.Value.RowId == 0 || owner.Value.RowId == territory.RowId)
            return territory.RowId;

        var placed = territory.PlaceName.ValueNullable?.Name.ExtractText();
        var owned = owner.Value.PlaceName.ValueNullable?.Name.ExtractText();
        return placed != null && placed == owned ? owner.Value.RowId : territory.RowId;
    }

    // Nearest point on the same map by map-coordinate distance; maps without
    // any marker (interiors, sub-zones like the Steps of Thal) fall back to
    // the zone's designated aetheryte, which TerritoryType names for every
    // town and field territory.
    private NearestAetheryte? NearestAetheryte(uint territoryId, uint mapId, float mapX, float mapY)
    {
        var zoneAetheryte = Sheet<TerritoryType>().GetRowOrDefault(territoryId)?.Aetheryte.ValueNullable;
        var zoneName = zoneAetheryte is { IsAetheryte: true } za ? za.PlaceName.ValueNullable?.Name.ExtractText() : null;
        var zoneRowId = zoneName is { Length: > 0 } ? zoneAetheryte!.Value.RowId : 0;

        if (!AetheryteMarkers().TryGetValue(mapId, out var markers))
            return zoneRowId == 0 ? null : new NearestAetheryte(zoneName!, false, zoneRowId, zoneName!);

        AetheryteMarker? best = null;
        var bestDistance = float.MaxValue;
        AetheryteMarker? bestAetheryte = null;
        var bestAetheryteDistance = float.MaxValue;
        foreach (var marker in markers)
        {
            var dx = marker.MapX - mapX;
            var dy = marker.MapY - mapY;
            var distance = (dx * dx) + (dy * dy);
            if (distance < bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }

            if (!marker.Aethernet && distance < bestAetheryteDistance)
            {
                bestAetheryte = marker;
                bestAetheryteDistance = distance;
            }
        }

        if (best is not { } nearest)
            return zoneRowId == 0 ? null : new NearestAetheryte(zoneName!, false, zoneRowId, zoneName!);

        if (!nearest.Aethernet)
            return new NearestAetheryte(nearest.Name, false, nearest.AetheryteRowId, nearest.Name);

        // A shard is reached by teleporting to the zone's aetheryte first.
        var (teleportId, teleportName) = zoneRowId != 0
            ? (zoneRowId, zoneName!)
            : bestAetheryte is { } fallback ? (fallback.AetheryteRowId, fallback.Name) : (0u, "");
        return new NearestAetheryte(nearest.Name, true, teleportId, teleportName);
    }

    private Dictionary<uint, List<AetheryteMarker>> AetheryteMarkers()
    {
        var markers = aetherytesByMap;
        if (markers == null)
        {
            lock (aetheryteLock)
            {
                markers = aetherytesByMap ??= BuildAetheryteMarkers();
            }
        }

        return markers;
    }

    private Dictionary<uint, List<AetheryteMarker>> BuildAetheryteMarkers()
    {
        var byMap = new Dictionary<uint, List<AetheryteMarker>>();
        var markers = SubrowSheet<MapMarker>();
        var aetherytes = Sheet<Aetheryte>();
        var places = Sheet<PlaceName>();
        foreach (var map in Sheet<Map>())
        {
            var range = markers.GetRowOrDefault(map.MapMarkerRange);
            if (range == null)
                continue;

            foreach (var marker in range.Value)
            {
                string name;
                var aetheryteRowId = 0u;
                if (marker.DataType == MapMarkerAetheryte)
                {
                    var aetheryte = aetherytes.GetRowOrDefault(marker.DataKey.RowId);
                    if (aetheryte is not { IsAetheryte: true } a ||
                        a.PlaceName.ValueNullable is not { } place || place.Name.IsEmpty)
                        continue;

                    name = place.Name.ExtractText();
                    aetheryteRowId = a.RowId;
                }
                else if (marker.DataType == MapMarkerAethernet)
                {
                    var place = places.GetRowOrDefault(marker.DataKey.RowId);
                    if (place == null || place.Value.Name.IsEmpty)
                        continue;

                    name = place.Value.Name.ExtractText();
                }
                else
                {
                    continue;
                }

                if (!byMap.TryGetValue(map.RowId, out var list))
                    byMap[map.RowId] = list = [];

                list.Add(new AetheryteMarker(
                    aetheryteRowId,
                    name,
                    aetheryteRowId == 0,
                    MapCoordConverter.FromMapPixel(marker.X, map.SizeFactor),
                    MapCoordConverter.FromMapPixel(marker.Y, map.SizeFactor)));
            }
        }

        return byMap;
    }

    private readonly record struct AetheryteMarker(
        uint AetheryteRowId, string Name, bool Aethernet, float MapX, float MapY);

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
