using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// Nearest aetheryte and public-territory fixes (store .Locations.cs).
public partial class GameDataCanaryTests
{
    // Checks the MapMarker-based aetheryte join (DataType 3 -> Aetheryte row,
    // pixel coords through FromMapPixel) and the nearest-by-distance pick: the
    // Western Thanalan iron ore node sits closer to Horizon than to any other
    // aetheryte on that map.
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

    // Checks the aethernet-shard branch (MapMarker DataType 4 -> PlaceName) and
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
    // will do; the first few found are enough to check the fallback.
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

    // Coverage check: nearly every quest start should name a nearest aetheryte,
    // through a same-map marker or the territory fallback. A patch that
    // reshapes MapMarker or TerritoryType.Aetheryte drops this.
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
}
