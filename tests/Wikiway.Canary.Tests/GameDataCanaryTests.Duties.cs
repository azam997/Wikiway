using Wikiway.Core.Abstractions;
using Wikiway.GameData;
using Xunit;

namespace Wikiway.Canary.Tests;

// Duty classification, unlock quests, MSQ gates and the solo-duty lookup (store
// .Duties.cs).
public partial class GameDataCanaryTests
{
    // Checks the curated field-area unlock table: Bozja carries no quest-typed
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
    // future patch must fail here - either its UnlockCriteria resolves natively
    // or FieldAreaUnlockQuests needs an entry checked against the sheets. A
    // zone that resolves nothing demotes to plain Duty, so kinds are checked
    // too.
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

    // Checks the script-arg unlock chain: the Wanderer's Palace carries no
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

    // The counterpart check: an MSQ-gated dungeon stays a plain Duty but still
    // names its unlock quest.
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

    // Spoiler gating check: an MSQ-unlocked dungeon's gate is the unlock quest
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

    // Checks the curated non-duty zones and their gate quests (each one
    // verified against the sheets when it was added).
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

    // Checks the content-type heuristic: 0 means the column read broke,
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

    // The light territory path used at zone-in skips the unlock-chain
    // resolution; it must never disagree with the full duty card. It runs on
    // the game thread with no warm-up, so a fresh store must answer at once.
    [Fact]
    public void SoloDutyTerritoryPathAgreesWithFullResolution()
    {
        var store = fixture.Store();
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

        Assert.NotNull(new LuminaGameDataStore(fixture.GameData!).FindSoloDutyName(soloTerritory));

        var dungeon = cfc.First(r =>
            !r.Name.IsEmpty && r.Name.ExtractText().Contains("Sastasha", StringComparison.OrdinalIgnoreCase));
        Assert.Null(store.FindSoloDutyName(dungeon.TerritoryType.RowId));
    }
}
