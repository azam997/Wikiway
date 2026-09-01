using System.Diagnostics;
using Wikiway.Core.Abstractions;
using Wikiway.GameData;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Core.Providers;
using Xunit;

namespace Wikiway.Canary.Tests;

// These load the real game files. If a patch breaks a sheet or a join we rely
// on, something here should go red before anyone notices in-game.
[Collection("gamedata")]
[Trait("Category", "GameData")]
public class GameDataCanaryTests(GameDataFixture fixture)
{
    [Fact]
    public void ItemSheetLoadsAndGilIsRowOne()
    {
        var store = fixture.Store();

        var gil = store.GetItem(1);
        Assert.NotNull(gil);
        Assert.Contains("gil", gil.Name, StringComparison.OrdinalIgnoreCase);
        Assert.True(gil.Icon > 0, "gil has no icon - Icon column read broke");
    }

    [Fact]
    public void NameIndexHasSaneFloorsPerKind()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        Assert.True(Count(names, EntityKind.Item) > 20_000, "item names collapsed");
        Assert.True(Count(names, EntityKind.Npc) > 1_000, "npc names collapsed");
        Assert.True(Count(names, EntityKind.Quest) > 4_000, "quest names collapsed");
        Assert.True(Count(names, EntityKind.Mount) > 100, "mount names collapsed");
        Assert.True(Count(names, EntityKind.Minion) > 400, "minion names collapsed");
        Assert.True(Count(names, EntityKind.Achievement) > 2_000, "achievement names collapsed");
        Assert.True(Count(names, EntityKind.Unlockable) > 150, "unlockable names collapsed");
        Assert.True(Count(names, EntityKind.Gatherable) > 500, "gatherable names collapsed");
        Assert.True(Count(names, EntityKind.Orchestrion) > 800, "orchestrion names collapsed");
        Assert.True(Count(names, EntityKind.TripleTriadCard) > 400, "triple triad card names collapsed");
        Assert.True(Count(names, EntityKind.Emote) > 150, "emote names collapsed");
        Assert.True(Count(names, EntityKind.Vista) > 300, "vista names collapsed");
        Assert.True(Count(names, EntityKind.HuntMark) > 250, "hunt mark names collapsed");
        Assert.True(Count(names, EntityKind.AetherCurrentZone) > 50, "aether current zone names collapsed");
        Assert.True(Count(names, EntityKind.Fate) > 1_500, "fate names collapsed");
        Assert.True(Count(names, EntityKind.Leve) > 1_500, "leve names collapsed");
    }

    [Fact]
    public void NameIndexBuildsQuickly()
    {
        var store = fixture.Store();

        var watch = Stopwatch.StartNew();
        var names = store.GetAllNames();
        watch.Stop();

        Assert.True(names.Count > 30_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"index build took {watch.Elapsed}");
    }

    // The most breakage-prone path we have: ENpcResident -> Level -> Map ->
    // TerritoryType -> PlaceName, plus the coord math. Momodi has stood at the
    // Quicksand counter since 2013; if she moved, it's the schema that moved.
    [Fact]
    public void MomodiResolvesWithPlausibleMapCoords()
    {
        var store = fixture.Store();

        var momodi = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Npc && n.Name.StartsWith("momodi"));
        Assert.NotNull(momodi);

        var npc = store.GetNpc(momodi.RowId);
        Assert.NotNull(npc);
        Assert.NotNull(npc.Location);

        Assert.InRange(npc.Location.MapX, 1, 45);
        Assert.InRange(npc.Location.MapY, 1, 45);
        Assert.False(string.IsNullOrEmpty(npc.Location.ZoneName));
        Assert.Contains("ul'dah", npc.Location.ZoneName, StringComparison.OrdinalIgnoreCase);
    }

    // Pins the sheets-first acquisition chain: Recipe results, GilShopItem ->
    // GilShop handler -> vendor NPC -> Level location. Iron Ingot has had two
    // recipes and gil vendors since 2013.
    [Fact]
    public void IronIngotAcquisitionResolvesFromSheets()
    {
        var store = fixture.Store();

        var item = store.GetItem(5057);
        Assert.NotNull(item);
        Assert.Equal("Iron Ingot", item.Name);
        Assert.NotNull(item.Acquisition);

        Assert.InRange(item.Acquisition.Recipes.Count, 2, 4);
        Assert.All(item.Acquisition.Recipes, r =>
        {
            Assert.False(string.IsNullOrEmpty(r.CraftType));
            Assert.InRange(r.Level, 10, 20);
            Assert.Contains(r.Ingredients, i => i.Name.Contains("Iron Ore") && i.Amount > 0);
        });

        var located = item.Acquisition.Vendors.Where(v => v.Location != null).ToList();
        Assert.True(located.Count >= 3, $"expected 3+ located vendors, got {located.Count}");
        Assert.Contains(located, v => v.Location!.ZoneName.Contains("Thanalan", StringComparison.OrdinalIgnoreCase));
        Assert.All(item.Acquisition.Vendors, v => Assert.Equal(68u, v.GilPrice));
    }

    // Pins the SpecialShop exchange chain: receive-item reverse index ->
    // 0x1B0000 handler block -> vendor NPC. Iron Ingot has traded 1:1 for a
    // Steel Amalj'ok at the Amalj'aa beast tribe since 2013.
    [Fact]
    public void IronIngotExchangeResolvesFromSpecialShops()
    {
        var store = fixture.Store();

        var item = store.GetItem(5057);
        Assert.NotNull(item?.Acquisition);

        Assert.InRange(item.Acquisition.Exchanges.Count, 1, 10);
        Assert.All(item.Acquisition.Exchanges, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.ShopName));
            Assert.False(string.IsNullOrEmpty(e.NpcName));
            Assert.NotEmpty(e.Costs);
        });
        Assert.Contains(item.Acquisition.Exchanges,
            e => e.Costs.Any(offer => offer.Any(p =>
                p.Name.Contains("Steel Amalj'ok", StringComparison.OrdinalIgnoreCase) && p.Amount > 0)));
    }

    // Pins the gathering chain: GatheringItem -> GatheringPointBase ->
    // GatheringPoint zone + ExportedGatheringPoint coords. Iron Ore mining
    // nodes have sat in Western Thanalan since 2013.
    [Fact]
    public void IronOreGatheringResolvesFromSheets()
    {
        var store = fixture.Store();

        var ironOre = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Gatherable && n.Name == "iron ore");
        Assert.NotNull(ironOre);

        var item = store.GetItem(ironOre.RowId);
        Assert.NotNull(item?.Acquisition);

        Assert.InRange(item.Acquisition.Gathering.Count, 1, 8);
        Assert.All(item.Acquisition.Gathering, g =>
        {
            Assert.Contains("Mining", g.NodeType, StringComparison.OrdinalIgnoreCase);
            Assert.InRange(g.Level, 10, 25);
        });
        Assert.Contains(item.Acquisition.Gathering, g =>
            g.Location is { } loc &&
            loc.ZoneName.Contains("Thanalan", StringComparison.OrdinalIgnoreCase) &&
            loc.MapX is > 1 and < 45 && loc.MapY is > 1 and < 45);
    }

    // Pins the master-book join: Recipe.SecretRecipeBook -> name + book item.
    // Heavy Wolfram gear has required Master Armorer I since 2.2, and the book
    // itself is an item, so its acquisition resolves like any other.
    [Fact]
    public void HeavyWolframHelmRecipeCarriesItsMasterBook()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        var helm = names.FirstOrDefault(n => n.Kind == EntityKind.Item && n.Name == "heavy wolfram helm");
        Assert.NotNull(helm);

        var item = store.GetItem(helm.RowId);
        Assert.NotNull(item?.Acquisition);
        Assert.NotEmpty(item.Acquisition.Recipes);
        Assert.All(item.Acquisition.Recipes, r => Assert.Equal("Master Armorer I", r.MasterBook));

        Assert.Contains(names, n => n.Kind == EntityKind.Item && n.Name == "master armorer i");
    }

    // Pins the timed-node join and the Eorzean clock math: GatheringPoint ->
    // GatheringPointTransient -> GatheringRarePopTimeTable. Spruce Log's
    // unspoiled node has popped at 9:00 ET since 2.0.
    [Fact]
    public void SpruceLogNodeCarriesItsUnspoiledWindow()
    {
        var store = fixture.Store();

        var log = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Gatherable && n.Name == "spruce log");
        Assert.NotNull(log);

        var item = store.GetItem(log.RowId);
        Assert.NotNull(item?.Acquisition);
        Assert.Contains(item.Acquisition.Gathering, g => g.TimeWindow == "Unspoiled · 9:00-12:00 ET");
    }

    // Same join, ephemeral branch (EphemeralStartTime/EndTime). Windtea Leaves
    // has been the 16:00 ET Sea of Clouds ephemeral node since Heavensward.
    [Fact]
    public void WindteaLeavesNodeCarriesItsEphemeralWindow()
    {
        var store = fixture.Store();

        var leaves = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Gatherable && n.Name == "windtea leaves");
        Assert.NotNull(leaves);

        var item = store.GetItem(leaves.RowId);
        Assert.NotNull(item?.Acquisition);
        Assert.Contains(item.Acquisition.Gathering, g => g.TimeWindow == "Ephemeral · 16:00-20:00 ET");
    }

    // Pins the fishing-spot reverse index and the map-pixel coord conversion:
    // FishingSpot.X/Z are map-image pixels, not world units, so a formula
    // regression would drift these coords off the Limsa docks. The Lominsan
    // Anchovy has swum there since 2013, and its notebook prose rides along.
    [Fact]
    public void LominsanAnchovyFishingResolvesFromSheets()
    {
        var store = fixture.Store();

        var item = store.GetItem(4870);
        Assert.NotNull(item);
        Assert.Equal("Lominsan Anchovy", item.Name);
        Assert.NotNull(item.Acquisition);

        Assert.InRange(item.Acquisition.Fishing.Count, 5, 12);
        Assert.All(item.Acquisition.Fishing, s => Assert.False(s.Spearfishing));
        Assert.Contains(item.Acquisition.Fishing, s =>
            s.SpotName == "Limsa Lominsa Lower Decks" &&
            s.Location is { } loc &&
            loc.MapX is > 7f and < 9f && loc.MapY is > 11f and < 13f);
        Assert.Contains("Qiqirn", item.Acquisition.FishingNote);
    }

    // Spearfishing rides a different chain: SpearfishingItem row ids sit in
    // the base's Item slots and the notebook row carries the gig spot. The
    // Wentletrap has been gigged in the Ruby Sea since Stormblood.
    [Fact]
    public void WentletrapSpearfishingResolvesFromSheets()
    {
        var store = fixture.Store();

        var wentletrap = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Item && n.Name == "wentletrap");
        Assert.NotNull(wentletrap);

        var item = store.GetItem(wentletrap.RowId);
        Assert.NotNull(item?.Acquisition);
        Assert.Contains(item.Acquisition.Fishing, s =>
            s.Spearfishing &&
            s.Level >= 60 &&
            s.Location is { } loc &&
            loc.ZoneName.Contains("Ruby Sea", StringComparison.OrdinalIgnoreCase));
    }

    // Pins the seal-shop chain: GCScripShopItem -> category -> company ->
    // GCShop handler (0x160000 block) -> quartermaster NPC. All three
    // companies have sold Ventures at 200 seals since retainers got ambitions.
    [Fact]
    public void VentureSealVendorsResolveAllThreeQuartermasters()
    {
        var store = fixture.Store();

        var item = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Item && n.Name == "venture")
            .Select(n => store.GetItem(n.RowId))
            .FirstOrDefault(i => i?.Acquisition?.SealVendors.Count > 0);
        Assert.NotNull(item);

        var vendors = item.Acquisition!.SealVendors;
        Assert.Equal(3, vendors.Count);
        Assert.All(vendors, v =>
        {
            Assert.Contains("Quartermaster", v.NpcName);
            Assert.Equal(200u, v.SealCost);
            Assert.NotNull(v.Location);
        });
    }

    // Rank names ride per-company text sheets keyed by GrandCompany row id
    // (1 Maelstrom -> Limsa, 2 Adder -> Gridania, 3 Flames -> Ul'dah); the
    // Maelstrom-only barding pins the pairing via its "Storm ..." rank.
    [Fact]
    public void RankGatedSealItemCarriesItsCompanyRankName()
    {
        var store = fixture.Store();

        var barding = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Item && n.Name == "lominsan half barding");
        Assert.NotNull(barding);

        var item = store.GetItem(barding.RowId);
        Assert.NotNull(item?.Acquisition);

        var vendor = Assert.Single(item.Acquisition.SealVendors);
        Assert.Equal("Storm Quartermaster", vendor.NpcName);
        Assert.StartsWith("Storm", vendor.RequiredRank);
    }

    // Pins the venture join: RetainerTaskNormal reverse index -> RetainerTask
    // level and class. Iron Ore has been a level 14 MIN venture with 15-50
    // yield bands since retainers learned to swing a pickaxe.
    [Fact]
    public void IronOreVentureResolvesFromSheets()
    {
        var store = fixture.Store();

        var item = store.GetItem(5111);
        Assert.NotNull(item?.Acquisition);

        var venture = Assert.Single(item.Acquisition.Ventures);
        Assert.Equal("MIN", venture.Category);
        Assert.Equal(14, venture.Level);
        Assert.Equal("15-50", venture.Quantities);
        Assert.InRange(venture.VentureCost, 1u, 2u);
    }

    // Pins the curated field-area unlock table: Bozja carries no quest-typed
    // UnlockCriteria in the sheets, so the table supplies Where Eagles Nest.
    [Fact]
    public void BozjanSouthernFrontUnlockResolvesFromCuratedTable()
    {
        var store = fixture.Store();

        var area = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name.Contains("bozjan southern front"));
        Assert.NotNull(area);

        var duty = store.GetDuty(area.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.FieldArea);
        Assert.True(duty.Optional);
        Assert.NotNull(duty.UnlockQuest);
        Assert.Equal("Where Eagles Nest", duty.UnlockQuest.Name);
        Assert.NotNull(duty.ChainStart);

        var quest = store.GetQuest(duty.UnlockQuest.RowId);
        Assert.NotNull(quest);
        Assert.InRange(quest.UnlockChains.Sum(c => c.Steps.Count), 1, 30);
    }

    // Intentionally exact, unlike the banded facts: a new field zone in a
    // future patch must fail here - either its UnlockCriteria resolves
    // natively or FieldAreaUnlockQuests needs a probe-verified entry. A zone
    // that resolves nothing demotes to plain Duty, so kinds are checked too.
    [Fact]
    public void EveryFieldAreaResolvesAnUnlockQuest()
    {
        var store = fixture.Store();

        var areas = store.GetAllNames()
            .Where(n => n.Kind is EntityKind.Duty or EntityKind.Unlockable)
            .Select(n => (Entry: n, Duty: store.GetDuty(n.RowId)))
            .Where(x => x.Duty is { FieldArea: true })
            .ToList();
        Assert.NotEmpty(areas);
        Assert.All(areas, a =>
        {
            Assert.True(a.Duty!.UnlockQuest != null,
                $"area '{a.Entry.Name}' ({a.Entry.RowId}) resolves no unlock quest");
            Assert.Equal(EntityKind.Unlockable, a.Entry.Kind);
        });
    }

    // Pins the script-arg unlock chain: the Wanderer's Palace carries no
    // UnlockCriteria; its gate is the side quest naming it in an
    // INSTANCEDUNGEON arg. Unchanged since 2013.
    [Fact]
    public void WanderersPalaceUnlocksViaItsSideQuest()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "the wanderer's palace");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.Optional);
        Assert.False(duty.FieldArea);
        Assert.Equal("Method in His Malice", duty.UnlockQuest?.Name);
    }

    // The complementary pin: an MSQ-gated dungeon stays a plain Duty but
    // still names its unlock quest.
    [Fact]
    public void SastashaStaysADutyDespiteItsMsqUnlock()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Duty && n.Name == "sastasha");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.False(duty.Optional);
        Assert.Equal("It's Probably Pirates", duty.UnlockQuest?.Name);
    }

    // Spoiler gating pin: an MSQ-unlocked dungeon's gate is the unlock quest
    // itself.
    [Fact]
    public void SastashaMsqGateIsItsOwnUnlockQuest()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Duty && n.Name == "sastasha");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.NotNull(duty.MsqGate);
        Assert.Equal(duty.UnlockQuest?.RowId, duty.MsqGate.Quest.RowId);
        Assert.Equal("2.x", duty.MsqGate.Version);
    }

    // The Wanderer's Palace chains back through the Zodiac relic line and
    // never touches MSQ (probed: ends at "Up in Arms"), so its gate is null
    // and spoiler gating fails open for it.
    [Fact]
    public void WanderersPalaceHasNoMsqGate()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "the wanderer's palace");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.Null(duty.MsqGate);
    }

    // A side-quest-unlocked duty whose chain does fork into MSQ gets the
    // marker as its gate, never the side quest itself.
    [Fact]
    public void OccultCrescentMsqGateSitsDeeperThanItsUnlockQuest()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "the occult crescent: south horn");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.NotNull(duty.MsqGate);
        Assert.NotEqual(duty.UnlockQuest?.RowId, duty.MsqGate.Quest.RowId);
        Assert.Equal("7.x", duty.MsqGate.Version);
    }

    // Raid coverage: each Alexander floor is gated by its own side quest.
    [Fact]
    public void AlexanderFistOfTheFatherUnlocksViaDisarmed()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "alexander - the fist of the father");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.Optional);
        Assert.Equal("Disarmed", duty.UnlockQuest?.Name);
    }

    // Deep-dungeon coverage: every Palace of the Dead floor set carries a
    // native quest-typed UnlockCriteria (10 sets as of 7.3).
    [Fact]
    public void PalaceOfTheDeadFloorsAreUnlockable()
    {
        var store = fixture.Store();

        var floors = store.GetAllNames()
            .Where(n => n.Name.StartsWith("the palace of the dead"))
            .ToList();
        Assert.InRange(floors.Count, 10, 20);
        Assert.All(floors, f => Assert.Equal(EntityKind.Unlockable, f.Kind));
    }

    // Pins the curated non-duty zones and their probe-verified gate quests.
    [Fact]
    public void CuratedZonesResolveTheirUnlockQuests()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        var expected = new (string Zone, string Quest)[]
        {
            ("the firmament", "Towards the Firmament"),
            ("the gold saucer", "It Could Happen to You"),
            ("island sanctuary", "Seeking Sanctuary"),
        };

        foreach (var (zone, quest) in expected)
        {
            var entry = names.FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == zone);
            Assert.True(entry != null, $"curated zone '{zone}' missing from the index");

            var duty = store.GetDuty(entry.RowId);
            Assert.NotNull(duty);
            Assert.True(duty.FieldArea);
            Assert.True(duty.Optional);
            Assert.Equal(quest, duty.UnlockQuest?.Name);
        }
    }

    // Pins the scene-spawn filter: Momodi has quest-scene copies in other
    // cities (single-quest event handlers) that must never surface as
    // flaggable locations - only the Quicksand post survives the merge.
    [Fact]
    public void MomodiCollapsesToHerQuicksandPostOnly()
    {
        var store = fixture.Store();

        var cards = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Npc && n.Name == "momodi")
            .Select(n => store.GetNpc(n.RowId))
            .Where(n => n != null)
            .Select(SearchResult (n) => new EntityCardResult
            {
                Title = n!.Name,
                Source = new Citation("Game data"),
                Entity = n,
                Score = 1.0,
            })
            .ToList();

        Assert.True(cards.Count > 1, "expected multiple momodi copies in ENpcResident");

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(cards)));
        var location = Assert.Single(card.MergedLocations);
        Assert.Contains("ul'dah", location.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.True(card.MergedHidden > 0, "expected scene copies to be hidden");

        // The hidden single-handler copies now name their gating quests.
        Assert.NotEmpty(card.CutsceneAppearances);
        Assert.All(card.CutsceneAppearances, a =>
        {
            Assert.False(string.IsNullOrEmpty(a.Quest.Name));
            Assert.False(string.IsNullOrEmpty(a.Expansion));
        });
        Assert.Contains(card.CutsceneAppearances, a => a.Location != null);
    }

    // Pins the story-NPC pin wipe: Y'shtola leaves a placement per MSQ stage
    // (probed 2026-08-23: 415 rows, 136 with 2+ handlers across 56 zones) and
    // no handler pattern marks the current one, so the card must show no pins
    // at all - the wiki result answers "where is she", and her single-quest
    // copies still surface as gated cutscene appearances.
    [Fact]
    public void YshtolaCollapsesToASingleCardWithNoPins()
    {
        var card = CollapseNpc("y'shtola");

        Assert.True(card.MergedCount > 100, $"expected 100+ y'shtola copies, got {card.MergedCount}");
        Assert.Empty(card.MergedLocations);
        Assert.True(card.MergedHidden > 100, "expected the stage placements to be hidden");
        Assert.NotEmpty(card.CutsceneAppearances);
    }

    [Fact]
    public void RaubahnCollapsesToASingleCardWithNoPins()
    {
        var card = CollapseNpc("raubahn");

        Assert.InRange(card.MergedCount, 20, 300);
        Assert.Empty(card.MergedLocations);
        Assert.NotEmpty(card.CutsceneAppearances);
    }

    private EntityCardResult CollapseNpc(string name)
    {
        var store = fixture.Store();

        var cards = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Npc && n.Name == name)
            .Select(n => store.GetNpc(n.RowId))
            .Where(n => n != null)
            .Select(SearchResult (n) => new EntityCardResult
            {
                Title = n!.Name,
                Source = new Citation("Game data"),
                Entity = n,
                Score = 1.0,
            })
            .ToList();

        Assert.True(cards.Count > 1, $"expected multiple {name} copies in ENpcResident");
        return Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(cards)));
    }

    [Fact]
    public void UltimateWeaponQuestHasResolvablePrerequisites()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "the ultimate weapon");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.NotEmpty(quest.Prerequisites);

        // Walk the chain far enough to prove the refs actually resolve.
        var current = quest;
        for (var depth = 0; depth < 10 && current.Prerequisites.Count > 0; depth++)
        {
            var previous = store.GetQuest(current.Prerequisites[0].RowId);
            Assert.NotNull(previous);
            Assert.False(string.IsNullOrEmpty(previous.Name));
            current = previous;
        }
    }

    // Pins the unlock-chain path: CFC UnlockCriteria -> Quest -> PreviousQuest
    // walk with the MSQ boundary. South Horn is a field area whose chain is a
    // few side quests ending in a Dawntrail MSQ (7.x) marker as of patch 7.3.
    [Fact]
    public void OccultCrescentUnlockChainResolvesAndStopsAtMsq()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "the occult crescent: south horn");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.FieldArea, "field-area detection broke");
        Assert.NotNull(duty.UnlockQuest);
        Assert.Equal("Unfamiliar Territory", duty.UnlockQuest.Name);

        var quest = store.GetQuest(duty.UnlockQuest.RowId);
        Assert.NotNull(quest);
        var chain = Assert.Single(quest.UnlockChains);
        Assert.InRange(chain.Steps.Count, 2, 10);
        Assert.All(chain.Steps, s => Assert.False(string.IsNullOrEmpty(s.Quest.Name)));
        Assert.Equal("7.x", quest.MsqRequirement);
        Assert.Equal("7.x", chain.Gate?.Version);
        Assert.False(chain.Continues);

        // Quest.IssuerLocation -> Level -> map coords, and the duty's shortcut
        // to the chain's first pick-up-able quest.
        Assert.Equal(chain.Steps[0].Quest.Name, duty.ChainStart?.Name);
        Assert.All(chain.Steps, s => Assert.NotNull(s.StartLocation));
        Assert.NotNull(quest.StartLocation);
        Assert.InRange(quest.StartLocation.MapX, 1, 45);
        Assert.InRange(quest.StartLocation.MapY, 1, 45);
        Assert.False(string.IsNullOrEmpty(quest.StartLocation.ZoneName));
    }

    // "The Bozja Incident" forks at "Hail to the Queen": Shadowbringers MSQ
    // (5.x) AND the Return to Ivalice finale. Each branch must be its own
    // chain in play order, dependency chain listed before its spawner.
    [Fact]
    public void BozjaIncidentForksIntoMsqAndIvalice()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "the bozja incident");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.Equal("5.x", quest.MsqRequirement);
        Assert.Equal(2, quest.UnlockChains.Count);

        var ivalice = quest.UnlockChains[0];
        Assert.Equal("Return to Ivalice", ivalice.Genre);
        Assert.InRange(ivalice.Steps.Count, 6, 10);
        Assert.Equal("The City of Lost Angels", ivalice.Steps[^1].Quest.Name);
        Assert.Equal("4.x", ivalice.Gate?.Version);

        var trunk = quest.UnlockChains[1];
        Assert.Equal("Resistance Weapons", trunk.Genre);
        Assert.Equal(["Hail to the Queen", "Path to the Past"], trunk.Steps.Select(s => s.Quest.Name));
        Assert.Equal("5.x", trunk.Gate?.Version);
    }

    // ~360 quest-unlocked duties as of 7.3 (99 via UnlockCriteria, the rest via
    // INSTANCEDUNGEON script args); 0 means a read broke, thousands means one
    // of the checks went over-broad.
    [Fact]
    public void DutyUnlockQuestCountLandsInAPlausibleBand()
    {
        var store = fixture.Store();

        var unlocked = store.GetAllNames()
            .Where(n => n.Kind is EntityKind.Duty or EntityKind.Unlockable)
            .Select(n => store.GetDuty(n.RowId))
            .Count(d => d is { UnlockQuest: not null });

        Assert.InRange(unlocked, 250, 900);
    }

    // "Dawntrail" (the 7.0 finale) is itself MSQ, so the boundary rule
    // collapses its whole ancestry into MSQ marker steps and nothing else.
    // "The Ultimate Weapon" is unusable as an anchor - an event quest shares
    // the exact name and wins the index lookup.
    [Fact]
    public void MsqQuestChainCollapsesToItsPatchRequirement()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "dawntrail");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.True(quest.MainScenario, "MSQ category detection broke");
        Assert.NotEmpty(quest.UnlockChains);
        Assert.All(quest.UnlockChains, c =>
        {
            Assert.Empty(c.Steps);
            Assert.Equal("7.x", c.Gate?.Version);
        });
        Assert.Equal("7.x", quest.MsqRequirement);
    }

    [Fact]
    public void MountAndMinionSamplesResolve()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        var mount = store.GetMount(names.First(n => n.Kind == EntityKind.Mount).RowId);
        var minion = store.GetMinion(names.First(n => n.Kind == EntityKind.Minion).RowId);

        Assert.False(string.IsNullOrEmpty(mount?.Name));
        Assert.False(string.IsNullOrEmpty(minion?.Name));
        Assert.True(mount!.Icon > 0, "mount has no icon - Icon column read broke");
        Assert.True(minion!.Icon > 0, "minion has no icon - Icon column read broke");
    }

    [Fact]
    public void AchievementSampleHasDescription()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().First(n => n.Kind == EntityKind.Achievement);
        var achievement = store.GetAchievement(entry.RowId);

        Assert.NotNull(achievement);
        Assert.False(string.IsNullOrEmpty(achievement.Description));
    }

    // ~590 as of 7.3 - the optional ~270 live under EntityKind.Unlockable.
    [Fact]
    public void DutyNamesAreIndexed()
    {
        var store = fixture.Store();

        Assert.True(Count(store.GetAllNames(), EntityKind.Duty) > 400, "duty names collapsed");
    }

    [Fact]
    public void BowlOfEmbersResolvesAndRoundTripsItsTerritory()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Duty && n.Name == "the bowl of embers");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.False(string.IsNullOrEmpty(duty.ContentType));
        Assert.False(duty.Solo);
        Assert.True(duty.ClassJobLevel > 0);

        var roundTrip = store.FindDutyByTerritory(duty.TerritoryTypeId);
        Assert.NotNull(roundTrip);
        Assert.Equal(duty.RowId, roundTrip.RowId);
    }

    // Pins the content-type heuristic: 0 means the column read broke,
    // thousands means it went over-broad. 116 as of 7.3 (84 QB + 32 Carnivale).
    [Fact]
    public void SoloDutyDetectionLandsInAPlausibleBand()
    {
        var store = fixture.Store();

        var solos = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Duty)
            .Select(n => store.GetDuty(n.RowId))
            .Count(d => d is { Solo: true });

        Assert.InRange(solos, 80, 1000);
    }

    // The light territory path used at zone-in re-implements solo detection
    // without the unlock-chain resolution; the two must never disagree. It
    // also must not build the lookup itself (game-thread callback) - cold it
    // returns null, so warm the store first the way Plugin's WarmAll task does.
    [Fact]
    public void SoloDutyTerritoryPathAgreesWithFullResolution()
    {
        var store = fixture.Store();
        store.WarmAll(CancellationToken.None);
        var cfc = fixture.GameData!.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()!;

        var solos = 0;
        uint soloTerritory = 0;
        foreach (var row in cfc)
        {
            if (row.Name.IsEmpty || row.TerritoryType.RowId == 0 || row.ContentType.RowId is not (7 or 27))
                continue;

            // Another CFC row can win the territory lookup; only the winner
            // is comparable.
            if (store.FindSoloDutyName(row.TerritoryType.RowId) is not { } light)
                continue;

            solos++;
            soloTerritory = row.TerritoryType.RowId;
            var full = store.FindDutyByTerritory(row.TerritoryType.RowId);
            Assert.NotNull(full);
            Assert.True(full.Solo, $"{full.Name}: light path says solo, full resolution disagrees");
            Assert.Equal(full.Name, light);
        }

        Assert.True(solos > 80, $"only {solos} solo territories resolved - the light path drifted");

        // The zone-in callback contract: before WarmAll, the light path skips
        // rather than building the lookup on the game thread.
        Assert.Null(new LuminaGameDataStore(fixture.GameData!).FindSoloDutyName(soloTerritory));

        var dungeon = cfc.First(r =>
            !r.Name.IsEmpty && r.Name.ExtractText().Contains("Sastasha", StringComparison.OrdinalIgnoreCase));
        Assert.Null(store.FindSoloDutyName(dungeon.TerritoryType.RowId));
    }

    // The "cosmic" family is ~230 items at 7.x, all landing in one score
    // tier where ties break shortest-name-first - a cap below the family
    // size silently starves its long-named members (Operator's attire).
    [Fact]
    public async Task CosmicItemFamilyComesBackWhole()
    {
        var provider = new LocalGameDataProvider(fixture.Store());

        var result = await provider.SearchAsync(
            new NormalizedQuery("cosmic", "cosmic", QueryIntent.Unknown, SearchCategory.Items),
            CancellationToken.None);

        Assert.True(result.Results.Count > 150, $"only {result.Results.Count} cosmic items came back");
        Assert.Contains(result.Results,
            r => r.Title.Equals("Cosmic Operator's Attire", StringComparison.OrdinalIgnoreCase));
    }

    // Pins the equipment chain: EquipSlotCategory flags -> slot name,
    // ClassJobCategory, LevelItem, damage columns. The MRD starting weapon has
    // been an untouched level-1 two-hander since 2013. It carries no BaseParam
    // stats and can't be HQ, so phantom stats here mean a column read drifted.
    [Fact]
    public void WeatheredWarAxeEquipmentResolvesFromSheets()
    {
        var store = fixture.Store();

        var item = store.GetItem(1749);
        Assert.NotNull(item);
        Assert.Equal("Weathered War Axe", item.Name);
        Assert.NotNull(item.Equipment);
        Assert.Equal("Main Hand", item.Equipment.Slot);
        Assert.Equal(1, item.Equipment.EquipLevel);
        Assert.InRange(item.Equipment.ItemLevel, 1, 10);
        Assert.Contains("MRD", item.Equipment.ClassJobs);
        Assert.NotNull(item.Equipment.Weapon);
        Assert.InRange(item.Equipment.Weapon.PhysDamage, 5, 15);
        Assert.InRange(item.Equipment.Weapon.DelaySeconds, 1.0, 5.0);
        Assert.Empty(item.Equipment.Stats);
        Assert.False(item.Equipment.CanBeHq);
        Assert.StartsWith("BSM", item.Equipment.Repair);
    }

    // Pins the HQ-delta semantics: BaseParamSpecial rows 21/24 carry the HQ
    // defense bonuses on crafted armor (probed 2026-08-23). Bronze Cuirass has
    // been a level-15 CanBeHq armorer craft with two materia slots since 2013.
    [Fact]
    public void BronzeCuirassHqBonusesResolveFromSpecialParams()
    {
        var store = fixture.Store();

        var item = store.GetItem(3026);
        Assert.NotNull(item);
        Assert.Equal("Bronze Cuirass", item.Name);
        Assert.NotNull(item.Equipment);
        Assert.Equal("Body", item.Equipment.Slot);
        Assert.True(item.Equipment.CanBeHq);
        Assert.NotNull(item.Equipment.Defense);
        Assert.InRange(item.Equipment.Defense.Physical, 30, 50);
        Assert.InRange(item.Equipment.Defense.HqPhysBonus, 3, 8);
        Assert.InRange(item.Equipment.Defense.HqMagBonus, 3, 8);
        Assert.InRange(item.Equipment.Stats.Count, 2, 5);
        Assert.All(item.Equipment.Stats, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        Assert.InRange(item.Equipment.MateriaSlots, 1, 5);
        Assert.False(string.IsNullOrEmpty(item.Equipment.Repair));
    }

    // 28,993 as of 7.3. Non-gear must stay payload-free or every item card
    // grows a bogus EQUIPMENT block.
    [Fact]
    public void EquippableItemCountLandsInAPlausibleBand()
    {
        var store = fixture.Store();
        var items = fixture.GameData!.GetExcelSheet<Lumina.Excel.Sheets.Item>()!;

        var equippable = items.Count(r => r.EquipSlotCategory.RowId != 0);
        Assert.InRange(equippable, 20_000, 60_000);

        Assert.Null(store.GetItem(5057)!.Equipment);
    }

    // Pins the reverse ItemAction join for mounts (action type 1322, Data[0] =
    // Mount row) and the MountTransient lore text. Aithon has been taught by
    // its whistle since 2.x.
    [Fact]
    public void AithonMountResolvesItsWhistleAndLore()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Mount && n.Name == "aithon");
        Assert.NotNull(entry);

        var mount = store.GetMount(entry.RowId);
        Assert.NotNull(mount);
        Assert.NotNull(mount.TeachingItem);
        Assert.Equal("Aithon Whistle", mount.TeachingItem.Name);
        Assert.False(string.IsNullOrEmpty(mount.Description));
    }

    // Pins the minion join (action type 853) plus the CompanionTransient
    // battle-stat columns, probe-verified 2026-09-01: HP 400, 55/40/3.
    [Fact]
    public void WindupCursorMinionCarriesBattleStatsAndItsItem()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Minion && n.Name == "wind-up cursor");
        Assert.NotNull(entry);

        var minion = store.GetMinion(entry.RowId);
        Assert.NotNull(minion);
        Assert.NotNull(minion.TeachingItem);
        Assert.Equal("Wind-up Cursor", minion.TeachingItem.Name);
        Assert.NotNull(minion.BattleStats);
        Assert.Equal(400, minion.BattleStats.Hp);
        Assert.Equal(55, minion.BattleStats.Attack);
        Assert.Equal(40, minion.BattleStats.Defense);
        Assert.Equal(3, minion.BattleStats.Speed);
        Assert.Equal("The Finger", minion.BattleStats.SpecialAction);
        Assert.False(string.IsNullOrEmpty(minion.Description));
    }

    // Orchestrion rolls join through Item.AdditionalData (action type 25183),
    // not Data[0]. Row 1 has been "A Cold Wind" since the orchestrion shipped.
    [Fact]
    public void AColdWindOrchestrionRollResolvesFromItsItem()
    {
        var store = fixture.Store();

        var roll = store.GetOrchestrion(1);
        Assert.NotNull(roll);
        Assert.Equal("A Cold Wind", roll.Name);
        Assert.NotNull(roll.TeachingItem);
        Assert.Equal("A Cold Wind Orchestrion Roll", roll.TeachingItem.Name);
        Assert.Contains("MGP", roll.Description);
        Assert.False(string.IsNullOrEmpty(roll.Category));
    }

    // Pins TripleTriadCardResident stats and the pack-purchase obtain label
    // (probe-verified 2026-09-01: 7/5/3/5, sale 300, obtain type 8).
    [Fact]
    public void MomodiModiCardCarriesStatsAndPackLabel()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "momodi modi");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Equal(7, card.Top);
        Assert.Equal(5, card.Bottom);
        Assert.Equal(3, card.Left);
        Assert.Equal(5, card.Right);
        Assert.InRange(card.Stars, 1, 5);
        Assert.Equal(300u, card.SaleValue);
        Assert.Contains("purchase", card.ObtainText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(card.TeachingItem);
        Assert.Equal("Momodi Modi Card", card.TeachingItem.Name);
    }

    // NPC-win obtain types carry an ENpcResident in Acquisition and a Level in
    // Location; the Vanu Vanu card has been won from Mogmill since 3.2.
    [Fact]
    public void VanuVanuCardIsWonFromMogmill()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "vanu vanu");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Equal("Mogmill", card.NpcName);
        Assert.NotNull(card.NpcLocation);
        Assert.Contains("Churning Mists", card.NpcLocation.ZoneName, StringComparison.OrdinalIgnoreCase);
    }

    // Duty-drop obtain types carry a ContentFinderCondition in Acquisition.
    [Fact]
    public void SahaginCardDropsInSastasha()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.TripleTriadCard && n.Name == "sahagin");
        Assert.NotNull(entry);

        var card = store.GetTripleTriadCard(entry.RowId);
        Assert.NotNull(card);
        Assert.Contains("Sastasha", card.DutyName, StringComparison.OrdinalIgnoreCase);
    }

    // Emote unlock bits (Emote.UnlockLink outside the quest id block) join to
    // the manual item whose type-2633 ItemAction sets the same bit.
    [Fact]
    public void BombDanceEmoteResolvesItsEtiquetteManual()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Emote && n.Name == "bomb dance");
        Assert.NotNull(entry);

        var emote = store.GetEmote(entry.RowId);
        Assert.NotNull(emote);
        Assert.Equal("/bombdance", emote.Command);
        Assert.Null(emote.UnlockQuest);
        Assert.NotNull(emote.TeachingItem);
        Assert.Equal("Ballroom Etiquette - The Bomb Dance", emote.TeachingItem.Name);
    }

    // UnlockLink values in the 0x10000 quest block are Quest row ids.
    [Fact]
    public void MoogleDanceEmoteUnlocksThroughItsQuest()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames().FirstOrDefault(n => n.Kind == EntityKind.Emote && n.Name == "moogle dance");
        Assert.NotNull(entry);

        var emote = store.GetEmote(entry.RowId);
        Assert.NotNull(emote);
        Assert.NotNull(emote.UnlockQuest);
        Assert.Contains("Piecing Together the Past", emote.UnlockQuest.Name);
    }

    // The four indirect shop-attachment paths (probed 2026-09-01): a direct
    // ENpcData handler reaches only ~31% of special shops, the rest hang off
    // TopicSelect menus, InclusionShop category trees, CustomTalk scripts,
    // and NPC-keyed FateShops.

    // TopicSelect: Sabina's 0x320002 menu carries the Gordian part shops.
    [Fact]
    public void PrototypeGordianArmetReachesSabinaThroughHerTopicMenu()
    {
        var store = fixture.Store();

        var item = store.GetItem(11448);
        Assert.NotNull(item?.Acquisition);
        Assert.Equal("Prototype Gordian Armet of Fending", item.Name);
        var sabina = item.Acquisition.Exchanges.FirstOrDefault(e => e.NpcName == "Sabina");
        Assert.NotNull(sabina);
        Assert.Contains("Gordian Part Exchange", sabina.ShopName);
        Assert.NotNull(sabina.Location);
        Assert.Contains("Idyllshire", sabina.Location.ZoneName, StringComparison.OrdinalIgnoreCase);
    }

    // FateShop rows are keyed by the vendor NPC id itself; the bicolor
    // gemstone traders have sold Berkanan Sap since 6.0.
    [Fact]
    public void BerkananSapResolvesTheGemstoneTraders()
    {
        var store = fixture.Store();

        var item = store.GetItem(36261);
        Assert.NotNull(item?.Acquisition);
        Assert.True(item.Acquisition.Exchanges.Count >= 3,
            $"expected 3+ gemstone traders, got {item.Acquisition.Exchanges.Count}");
        var gadfrid = item.Acquisition.Exchanges.FirstOrDefault(e => e.NpcName == "Gadfrid");
        Assert.NotNull(gadfrid);
        Assert.Contains("Sharlayan", gadfrid.Location?.ZoneName ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // TopicSelect again, at the other end of the game: the Calamity Salvager's
    // gold chocobo feather page.
    [Fact]
    public void AmberDraughtWhistleReachesTheCalamitySalvager()
    {
        var store = fixture.Store();

        var item = store.GetItem(12993);
        Assert.NotNull(item?.Acquisition);
        Assert.Contains(item.Acquisition.Exchanges,
            e => e.NpcName.Contains("Calamity Salvager", StringComparison.OrdinalIgnoreCase));
    }

    // InclusionShop: the scrip-exchange aggregator chains Category ->
    // InclusionShopSeries subrows -> SpecialShop.
    [Fact]
    public void HiCordialResolvesAScripExchangeShop()
    {
        var store = fixture.Store();

        var item = store.GetItem(12669);
        Assert.NotNull(item?.Acquisition);
        Assert.Contains(item.Acquisition.Exchanges,
            e => e.ShopName.Contains("Scrip Exchange", StringComparison.OrdinalIgnoreCase));
    }

    // Pins the reverse ingredient index. Iron Ingot has fed 100+ recipes for
    // years; a collapse here means the ingredient scan broke.
    [Fact]
    public void IronIngotUsageCountsItsRecipes()
    {
        var store = fixture.Store();

        var item = store.GetItem(5057);
        Assert.NotNull(item?.Usage);
        Assert.InRange(item.Usage.UsedInRecipes, 100, 400);
    }

    // Pins the custom-delivery chain: SatisfactionNpc.SatisfactionNpcParams
    // SupplyIndex -> SatisfactionSupply subrows -> item. Raven Coal has been a
    // Kai-Shirr miner turn-in since Shadowbringers.
    [Fact]
    public void RavenCoalIsWantedByKaiShirr()
    {
        var store = fixture.Store();

        var item = store.GetItem(28199);
        Assert.NotNull(item?.Usage);

        var delivery = item.Usage.Deliveries.SingleOrDefault(
            d => d.NpcName.Contains("Kai-Shirr", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(delivery);
        Assert.Equal("Oh, Beehive Yourself", delivery.UnlockQuest?.Name);
    }

    // Pins the CollectablesShopItem join and its scrip payout read; the scrip
    // type itself is not named in the sheets (opaque Currency id), so only the
    // amount is asserted.
    [Fact]
    public void RarefiedRaKaznarOreIsACollectableTurnIn()
    {
        var store = fixture.Store();

        var item = store.GetItem(43922);
        Assert.NotNull(item?.Usage);

        var turnIn = Assert.Single(item.Usage.CollectableTurnIns);
        Assert.Equal(100, turnIn.LevelMin);
        Assert.Equal(38, turnIn.MaxScrips);
    }

    // Pins TreasureHuntRank.ItemName -> TreasureSpot subrows -> Level zones.
    // Zonureskin maps have dug up 8 sites in each of 6 Shadowbringers zones
    // since 5.0.
    [Fact]
    public void TimewornZonureskinMapListsItsZones()
    {
        var store = fixture.Store();

        var item = store.GetItem(26745);
        Assert.NotNull(item?.Usage?.TreasureMap);

        var map = item.Usage.TreasureMap;
        Assert.Equal(8, map.PartySize);
        Assert.Equal(6, map.Zones.Count);
        Assert.All(map.Zones, z => Assert.Equal(8, z.SpotCount));
        Assert.Contains(map.Zones, z => z.ZoneName == "Lakeland");
        Assert.Contains(map.Zones, z => z.ZoneName == "The Tempest");
    }

    // Pins the ItemFood read (status + duration from ItemAction.Data, stats
    // with relative caps and HQ values from the shared ItemFood row).
    [Fact]
    public void BoiledEggCarriesItsFoodStats()
    {
        var store = fixture.Store();

        var item = store.GetItem(4650);
        Assert.NotNull(item?.Food);
        Assert.Equal("Well Fed", item.Food.StatusName);
        Assert.Equal(1800, item.Food.DurationSeconds);
        Assert.Equal(3, item.Food.ExpBonusPercent);

        var crit = item.Food.Stats.Single(s => s.Name == "Critical Hit");
        Assert.True(crit.Relative);
        Assert.Equal(8, crit.Value);
        Assert.Equal(2, crit.Max);
        Assert.Equal(10, crit.HqValue);
        Assert.Equal(3, crit.HqMax);

        var vitality = item.Food.Stats.Single(s => s.Name == "Vitality");
        Assert.False(vitality.Relative);
        Assert.Equal(1, vitality.Value);
    }

    // Medicine variant: short Medicated buff, no EXP bonus.
    [Fact]
    public void TinctureOfStrengthIsAShortMedicatedBuff()
    {
        var store = fixture.Store();

        var item = store.GetItem(27786);
        Assert.NotNull(item?.Food);
        Assert.Equal("Medicated", item.Food.StatusName);
        Assert.Equal(30, item.Food.DurationSeconds);
        Assert.Equal(0, item.Food.ExpBonusPercent);

        var strength = Assert.Single(item.Food.Stats);
        Assert.Equal("Strength", strength.Name);
        Assert.True(strength.Relative);
        Assert.Equal(94, strength.Max);
        Assert.Equal(117, strength.HqMax);
    }

    // Pins the Materia sheet reverse (stat name + per-grade value).
    [Fact]
    public void SavageAimMateriaNineCarriesItsMateriaTag()
    {
        var store = fixture.Store();

        var item = store.GetItem(33919);
        Assert.NotNull(item?.Usage);
        Assert.Equal("Critical Hit +12", item.Usage.MateriaTag);
    }

    // Pins the Adventure sheet reads end to end: hint vs lore column pick,
    // Level -> map coords, emote text command, and the ET window rendering
    // (800-1159 in the sheet is the 8:00-12:00 window).
    [Fact]
    public void BarracudaPiersVistaResolvesFromSheets()
    {
        var store = fixture.Store();

        var vista = store.GetVista(2162688);
        Assert.NotNull(vista);
        Assert.Equal("Barracuda Piers", vista.Name);
        Assert.Contains("warships", vista.Hint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Crimson Fleet", vista.Lore);
        Assert.Equal("Limsa Lominsa", vista.Region);
        Assert.Equal("/lookout", vista.Emote);
        Assert.Equal("8:00-12:00 ET", vista.TimeWindow);

        Assert.NotNull(vista.Location);
        Assert.Contains("Limsa", vista.Location.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(vista.Location.MapX, 9, 11);
        Assert.InRange(vista.Location.MapY, 7, 9);
    }

    // A window wrapping midnight (The Astalicia, 1800-459) must render as
    // 18:00-5:00, not fold into nonsense.
    [Fact]
    public void AstaliciaVistaWindowWrapsMidnight()
    {
        var store = fixture.Store();

        var vista = store.GetVista(2162689);
        Assert.NotNull(vista);
        Assert.Equal("18:00-5:00 ET", vista.TimeWindow);
    }

    // Rank byte -> letter (probed: 1=B, 2=A, 3=S) and the hunt-bill zone join.
    // Only billed marks carry a zone; Laideronnette has been the East Shroud
    // S rank since 2.0 and sits on no bill.
    [Fact]
    public void HuntMarkRanksAndZonesResolveFromSheets()
    {
        var store = fixture.Store();

        var laideronnette = store.GetHuntMark(3);
        Assert.NotNull(laideronnette);
        Assert.Equal("Laideronnette", laideronnette.Name);
        Assert.Equal("S", laideronnette.Rank);
        Assert.Equal("", laideronnette.ZoneName);

        var whiteJoker = store.GetAllNames()
            .First(n => n.Kind == EntityKind.HuntMark && n.Name == "white joker");
        var mark = store.GetHuntMark(whiteJoker.RowId);
        Assert.NotNull(mark);
        Assert.Equal("B", mark.Rank);
        Assert.Equal("Central Shroud", mark.ZoneName);
    }

    // Pins AetherCurrentCompFlgSet -> AetherCurrent.Quest with quest levels
    // and start-flag locations. Every flying zone since Heavensward grants
    // five currents from named quests.
    [Fact]
    public void KholusiaAetherCurrentsResolveTheirFiveQuests()
    {
        var store = fixture.Store();

        var zone = store.GetAllNames()
            .First(n => n.Kind == EntityKind.AetherCurrentZone && n.Name == "kholusia aether currents");
        var currents = store.GetAetherCurrentZone(zone.RowId);
        Assert.NotNull(currents);
        Assert.Equal("Kholusia", currents.Name);
        Assert.Equal(5, currents.QuestCurrents.Count);
        Assert.All(currents.QuestCurrents, q => Assert.False(string.IsNullOrEmpty(q.Quest.Name)));
        Assert.True(currents.QuestCurrents.Count(q => q.StartLocation != null) >= 4,
            "quest start locations collapsed");
    }

    // FATE cards are name + level + description; Location is an LGB id the
    // sheets cannot resolve, so no coordinates are asserted anywhere.
    [Fact]
    public void CleverGirlsFateResolvesFromSheets()
    {
        var store = fixture.Store();

        var fate = store.GetFate(120);
        Assert.NotNull(fate);
        Assert.Equal("Clever Girls", fate.Name);
        Assert.Equal(5, fate.Level);
        Assert.Contains("anoles", fate.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fate.RequiredQuest);
    }

    // The Bozja field fates are the quest-gated ones.
    [Fact]
    public void BozjanFateCarriesItsRequiredQuest()
    {
        var store = fixture.Store();

        var gated = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Fate)
            .Select(n => store.GetFate(n.RowId))
            .FirstOrDefault(f => f?.RequiredQuest != null);
        Assert.NotNull(gated);
        Assert.Equal("Where Eagles Nest", gated.RequiredQuest!.Name);
    }

    // Pins the Leve sheet reads: assignment type, job category, the direct
    // LevelLevemete flag, and reward columns. "In with the New" has been the
    // level 1 Gridania carpenter leve since 2.0.
    [Fact]
    public void InWithTheNewLeveResolvesItsLevemete()
    {
        var store = fixture.Store();

        var leve = store.GetLeve(21);
        Assert.NotNull(leve);
        Assert.Equal("In with the New", leve.Name);
        Assert.Equal(1, leve.Level);
        Assert.Equal("Carpenter", leve.Type);
        Assert.Equal("CRP", leve.JobCategory);
        Assert.Equal(1, leve.AllowanceCost);
        Assert.True(leve.ExpReward > 0);
        Assert.True(leve.GilReward > 0);

        Assert.NotNull(leve.Levemete);
        Assert.Contains("Gridania", leve.Levemete.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(leve.Levemete.MapX, 10, 13);
        Assert.InRange(leve.Levemete.MapY, 10, 13);
    }

    // Pins the MapMarker-based aetheryte join (DataType 3 -> Aetheryte row,
    // pixel coords through FromMapPixel) and the nearest-by-distance pick:
    // the Western Thanalan iron ore node sits closer to Horizon than to any
    // other aetheryte on that map.
    [Fact]
    public void IronOreNodeNamesHorizonAsItsNearestAetheryte()
    {
        var store = fixture.Store();

        var ironOre = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Gatherable && n.Name == "iron ore");
        Assert.NotNull(ironOre);

        var item = store.GetItem(ironOre.RowId);
        Assert.NotNull(item?.Acquisition);

        var node = item.Acquisition.Gathering.First(g =>
            g.Location is { } loc && loc.ZoneName.Contains("Western Thanalan", StringComparison.OrdinalIgnoreCase));
        var near = node.Location!.Aetheryte;
        Assert.NotNull(near);
        Assert.Equal("Horizon", near.Name);
        Assert.False(near.Aethernet);
        Assert.Equal(17u, near.TeleportRowId);
        Assert.Equal("Horizon", near.TeleportName);
    }

    // Pins the aethernet-shard branch (MapMarker DataType 4 -> PlaceName) and
    // the teleport target behind it: Momodi's counter is the Quicksand, whose
    // shard is the Adventurers' Guild, but teleporting still means the city's
    // own aetheryte.
    [Fact]
    public void MomodiSitsByTheAdventurersGuildShard()
    {
        var store = fixture.Store();

        var momodi = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Npc && n.Name.StartsWith("momodi"));
        Assert.NotNull(momodi);

        var npc = store.GetNpc(momodi.RowId);
        var near = npc?.Location?.Aetheryte;
        Assert.NotNull(near);
        Assert.Equal("Adventurers' Guild", near.Name);
        Assert.True(near.Aethernet);
        Assert.Equal(9u, near.TeleportRowId);
        Assert.Equal("Ul'dah - Steps of Nald", near.TeleportName);
    }

    // The Steps of Thal map carries shards but no teleport aetheryte, so the
    // teleport target has to come from TerritoryType.Aetheryte (probed 7.3:
    // every named town and field territory names one). Any NPC placed there
    // will do; the first few found pin the fallback.
    [Fact]
    public void StepsOfThalPlacementsTeleportViaTheUldahAetheryte()
    {
        var store = fixture.Store();
        const uint stepsOfThalMap = 14;

        var found = 0;
        foreach (var entry in store.GetAllNames().Where(n => n.Kind == EntityKind.Npc))
        {
            var npc = store.GetNpc(entry.RowId);
            if (npc?.Location is not { MapId: stepsOfThalMap } location)
                continue;

            Assert.NotNull(location.Aetheryte);
            Assert.True(location.Aetheryte.Aethernet, $"{npc.Name}: expected a shard on the Steps of Thal");
            Assert.Equal(9u, location.Aetheryte.TeleportRowId);
            if (++found == 3)
                break;
        }

        Assert.Equal(3, found);
    }

    // Quest-scene copies of a zone are separate TerritoryType rows sharing
    // the public map; a flag carrying the copy's id never matches where the
    // player stands. Row 1003988 is a Momodi scene placement in territory
    // 182 (probed 7.3), which must come out as the public Ul'dah row 130.
    [Fact]
    public void ScenePlacementsNormalizeToThePublicTerritory()
    {
        var store = fixture.Store();

        var copy = store.GetNpc(1003988);
        Assert.NotNull(copy?.Location);
        Assert.Equal(130u, copy.Location.TerritoryTypeId);
        Assert.Equal(13u, copy.Location.MapId);
    }

    // Coverage tripwire: nearly every quest start should name a nearest
    // aetheryte, through a same-map marker or the territory fallback. A
    // patch that reshapes MapMarker or TerritoryType.Aetheryte drops this.
    [Fact]
    public void AlmostEveryQuestStartKnowsItsNearestAetheryte()
    {
        var store = fixture.Store();

        var located = 0;
        var withAetheryte = 0;
        foreach (var entry in store.GetAllNames().Where(n => n.Kind == EntityKind.Quest).Take(600))
        {
            if (store.GetQuest(entry.RowId)?.StartLocation is not { } start)
                continue;

            located++;
            if (start.Aetheryte != null)
                withAetheryte++;
        }

        Assert.True(located > 400, $"only {located} located quest starts");
        Assert.True(withAetheryte >= located * 0.95, $"{withAetheryte}/{located} quest starts have a nearest aetheryte");
    }

    private static int Count(IReadOnlyList<NameIndexEntry> names, EntityKind kind) =>
        names.Count(n => n.Kind == kind);
}
