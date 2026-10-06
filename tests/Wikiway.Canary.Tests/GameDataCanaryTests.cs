using System.Diagnostics;
using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// These load the real game files. If a patch breaks a sheet or a join we rely
// on, something here should go red before anyone notices in-game.
[Collection("gamedata")]
[Trait("Category", "GameData")]
public partial class GameDataCanaryTests(GameDataFixture fixture)
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

    private static int Count(IReadOnlyList<NameIndexEntry> names, EntityKind kind) =>
        names.Count(n => n.Kind == kind);
}
