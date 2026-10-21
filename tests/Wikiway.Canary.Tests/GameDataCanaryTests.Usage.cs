using Xunit;

namespace Wikiway.Canary.Tests;

// The USAGE block (store .Usage.cs) and the materia tag (.Items.cs).
public partial class GameDataCanaryTests
{
    // Checks the ingredient count. Iron Ingot has fed 100+ recipes for years;
    // a collapse here means the ingredient scan broke.
    [Fact]
    public void IronIngotUsageCountsItsRecipes()
    {
        var store = fixture.Store();

        var details = store.GetItemDetails(5057);
        Assert.NotNull(details.Usage);
        Assert.InRange(details.Usage.UsedInRecipes, 100, 400);
    }

    // Checks the custom-delivery chain: SatisfactionNpc.SatisfactionNpcParams
    // SupplyIndex -> SatisfactionSupply subrows -> item. Raven Coal has been a
    // Kai-Shirr miner turn-in since Shadowbringers.
    [Fact]
    public void RavenCoalIsWantedByKaiShirr()
    {
        var store = fixture.Store();

        var details = store.GetItemDetails(28199);
        Assert.NotNull(details.Usage);

        var delivery = details.Usage.Deliveries.SingleOrDefault(
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

        var details = store.GetItemDetails(43922);
        Assert.NotNull(details.Usage);

        var turnIn = Assert.Single(details.Usage.CollectableTurnIns);
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

        var details = store.GetItemDetails(26745);
        Assert.NotNull(details.Usage?.TreasureMap);

        var map = details.Usage.TreasureMap;
        Assert.Equal(8, map.PartySize);
        Assert.Equal(6, map.Zones.Count);
        Assert.All(map.Zones, z => Assert.Equal(8, z.SpotCount));
        Assert.Contains(map.Zones, z => z.ZoneName == "Lakeland");
        Assert.Contains(map.Zones, z => z.ZoneName == "The Tempest");
    }

    // Checks the Materia sheet read (stat name + per-grade value).
    [Fact]
    public void SavageAimMateriaNineCarriesItsMateriaTag()
    {
        var store = fixture.Store();

        var item = store.GetItem(33919);
        Assert.NotNull(item);
        Assert.Equal("Critical Hit +12", item.MateriaTag);
    }

    // The store only scans the Materia sheet for items in the Materia UI
    // category (156 of 156 at 7.3). If a patch files a materia elsewhere, its
    // tag would silently vanish; this walks every tagged item to catch that.
    [Fact]
    public void EveryMateriaSheetItemCarriesItsTag()
    {
        var store = fixture.Store();
        var materiaSheet = fixture.GameData!.GetExcelSheet<Lumina.Excel.Sheets.Materia>()!;

        var tagged = 0;
        foreach (var materia in materiaSheet)
        {
            if (materia.BaseParam.ValueNullable is not { Name.IsEmpty: false })
                continue;

            for (var i = 0; i < materia.Item.Count && i < materia.Value.Count; i++)
            {
                if (materia.Item[i].RowId == 0 || materia.Value[i] == 0)
                    continue;

                var item = store.GetItem(materia.Item[i].RowId);
                Assert.NotNull(item);
                Assert.False(item.MateriaTag.Length == 0, $"{item.Name} lost its materia tag");
                tagged++;
            }
        }

        Assert.True(tagged > 100, $"only {tagged} tagged materia - the Materia sheet read broke");
    }
}
