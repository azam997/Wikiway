using System.Diagnostics;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
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
            Assert.Contains(r.Ingredients, i => i.Contains("Iron Ore"));
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
            e => e.Costs.Any(c => c.Contains("Steel Amalj'ok", StringComparison.OrdinalIgnoreCase)));
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
    // without the unlock-chain resolution; the two must never disagree.
    [Fact]
    public void SoloDutyTerritoryPathAgreesWithFullResolution()
    {
        var store = fixture.Store();
        var cfc = fixture.GameData!.GetExcelSheet<Lumina.Excel.Sheets.ContentFinderCondition>()!;

        var solos = 0;
        foreach (var row in cfc)
        {
            if (row.Name.IsEmpty || row.TerritoryType.RowId == 0 || row.ContentType.RowId is not (7 or 27))
                continue;

            // Another CFC row can win the territory lookup; only the winner
            // is comparable.
            if (store.FindSoloDutyName(row.TerritoryType.RowId) is not { } light)
                continue;

            solos++;
            var full = store.FindDutyByTerritory(row.TerritoryType.RowId);
            Assert.NotNull(full);
            Assert.True(full.Solo, $"{full.Name}: light path says solo, full resolution disagrees");
            Assert.Equal(full.Name, light);
        }

        Assert.True(solos > 80, $"only {solos} solo territories resolved - the light path drifted");

        var dungeon = cfc.First(r =>
            !r.Name.IsEmpty && r.Name.ExtractText().Contains("Sastasha", StringComparison.OrdinalIgnoreCase));
        Assert.Null(store.FindSoloDutyName(dungeon.TerritoryType.RowId));
    }

    private static int Count(IReadOnlyList<NameIndexEntry> names, EntityKind kind) =>
        names.Count(n => n.Kind == kind);
}
