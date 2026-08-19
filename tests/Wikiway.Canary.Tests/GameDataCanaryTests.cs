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
        Assert.True(Count(names, EntityKind.Area) > 5, "area names collapsed");
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
            .FirstOrDefault(n => n.Kind == EntityKind.Area && n.Name == "the occult crescent: south horn");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.FieldArea, "field-area detection broke");
        Assert.NotNull(duty.UnlockQuest);
        Assert.Equal("Unfamiliar Territory", duty.UnlockQuest.Name);

        var quest = store.GetQuest(duty.UnlockQuest.RowId);
        Assert.NotNull(quest);
        Assert.InRange(quest.UnlockChain.Count, 2, 10);
        Assert.All(quest.UnlockChain, s => Assert.False(string.IsNullOrEmpty(s.Quest.Name)));
        for (var i = 1; i < quest.UnlockChain.Count; i++)
            Assert.True(quest.UnlockChain[i].Depth >= quest.UnlockChain[i - 1].Depth, "chain depths regressed");
        Assert.Equal("7.x", quest.MsqRequirement);
        Assert.Equal("7.x", quest.UnlockChain[^1].MsqVersion);
        Assert.False(quest.ChainContinues);

        // Quest.IssuerLocation -> Level -> map coords, and the duty's shortcut
        // to the chain's first pick-up-able quest.
        var questSteps = quest.UnlockChain.Where(s => s.MsqVersion == null).ToList();
        Assert.NotEmpty(questSteps);
        Assert.Equal(questSteps[^1].Quest.Name, duty.ChainStart?.Name);
        Assert.All(questSteps, s => Assert.NotNull(s.StartLocation));
        Assert.NotNull(quest.StartLocation);
        Assert.InRange(quest.StartLocation.MapX, 1, 45);
        Assert.InRange(quest.StartLocation.MapY, 1, 45);
        Assert.False(string.IsNullOrEmpty(quest.StartLocation.ZoneName));
    }

    // "The Bozja Incident" forks at "Hail to the Queen": Shadowbringers MSQ
    // (5.x) AND the Return to Ivalice finale. The marker must sit at that
    // fork's depth beside its sibling, not trail the whole chain.
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

        var marker = quest.UnlockChain.FirstOrDefault(s => s.MsqVersion == "5.x");
        Assert.NotNull(marker);
        Assert.Contains(quest.UnlockChain,
            s => s.MsqVersion == null && s.Depth == marker.Depth && s.Quest.Name == "The City of Lost Angels");
        Assert.Contains(quest.UnlockChain, s => s.Depth > marker.Depth && s.MsqVersion == null);
    }

    // 100 quest-unlocked duties as of 7.3; 0 means the UnlockCriteria read
    // broke, thousands means the typed-link check went over-broad.
    [Fact]
    public void DutyUnlockQuestCountLandsInAPlausibleBand()
    {
        var store = fixture.Store();

        var unlocked = store.GetAllNames()
            .Where(n => n.Kind is EntityKind.Duty or EntityKind.Area)
            .Select(n => store.GetDuty(n.RowId))
            .Count(d => d is { UnlockQuest: not null });

        Assert.InRange(unlocked, 50, 500);
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
        Assert.NotEmpty(quest.UnlockChain);
        Assert.All(quest.UnlockChain, s => Assert.Equal("7.x", s.MsqVersion));
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

    [Fact]
    public void DutyNamesAreIndexed()
    {
        var store = fixture.Store();

        Assert.True(Count(store.GetAllNames(), EntityKind.Duty) > 800, "duty names collapsed");
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

    private static int Count(IReadOnlyList<NameIndexEntry> names, EntityKind kind) =>
        names.Count(n => n.Kind == kind);
}
