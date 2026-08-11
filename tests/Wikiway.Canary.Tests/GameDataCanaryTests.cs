using System.Diagnostics;
using Wikiway.Core.Abstractions;
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

    [Fact]
    public void MountAndMinionSamplesResolve()
    {
        var store = fixture.Store();
        var names = store.GetAllNames();

        var mount = names.First(n => n.Kind == EntityKind.Mount);
        var minion = names.First(n => n.Kind == EntityKind.Minion);

        Assert.False(string.IsNullOrEmpty(store.GetMount(mount.RowId)?.Name));
        Assert.False(string.IsNullOrEmpty(store.GetMinion(minion.RowId)?.Name));
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

    private static int Count(IReadOnlyList<NameIndexEntry> names, EntityKind kind) =>
        names.Count(n => n.Kind == kind);
}
