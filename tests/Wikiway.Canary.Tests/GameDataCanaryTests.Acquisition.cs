using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// Item sources: recipes, vendors, exchanges, nodes, fishing, GC seals, ventures
// (store .Acquisition.cs / .AcquisitionIndex.cs).
public partial class GameDataCanaryTests
{
    // Checks the sheets-first acquisition chain: Recipe results, GilShopItem ->
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

    // Checks the SpecialShop exchange chain: receive-item reverse index ->
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

    // Checks the gathering chain: GatheringItem -> GatheringPointBase ->
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

    // Checks the master-book join: Recipe.SecretRecipeBook -> name + book item.
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

    // Checks the timed-node join and the Eorzean clock math: GatheringPoint ->
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

    // Checks the fishing-spot reverse index and the map-pixel coord conversion:
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

    // Checks the seal-shop chain: GCScripShopItem -> category -> company ->
    // GCShop handler (0x160000 block) -> quartermaster NPC. All three companies
    // have sold Ventures at 200 seals since retainers got ambitions.
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

    // Rank names ride per-company text sheets keyed by GrandCompany row id (1
    // Maelstrom -> Limsa, 2 Adder -> Gridania, 3 Flames -> Ul'dah); the
    // Maelstrom-only barding confirms the pairing via its "Storm ..." rank.
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

    // Checks the venture join: RetainerTaskNormal reverse index -> RetainerTask
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
}
