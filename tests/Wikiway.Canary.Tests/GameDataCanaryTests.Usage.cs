using Xunit;

namespace Wikiway.Canary.Tests;

// The USAGE block's reverse indexes (store .Usage.cs).
public partial class GameDataCanaryTests
{
    // Checks the reverse ingredient index. Iron Ingot has fed 100+ recipes for
    // years; a collapse here means the ingredient scan broke.
    [Fact]
    public void IronIngotUsageCountsItsRecipes()
    {
        var store = fixture.Store();

        var item = store.GetItem(5057);
        Assert.NotNull(item?.Usage);
        Assert.InRange(item.Usage.UsedInRecipes, 100, 400);
    }

    // Checks the custom-delivery chain: SatisfactionNpc.SatisfactionNpcParams
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

    // Checks the CollectablesShopItem join and its scrip payout read; the scrip
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

    // Checks TreasureHuntRank.ItemName -> TreasureSpot subrows -> Level zones.
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

    // Checks the Materia sheet reverse (stat name + per-grade value).
    [Fact]
    public void SavageAimMateriaNineCarriesItsMateriaTag()
    {
        var store = fixture.Store();

        var item = store.GetItem(33919);
        Assert.NotNull(item?.Usage);
        Assert.Equal("Critical Hit +12", item.Usage.MateriaTag);
    }
}
