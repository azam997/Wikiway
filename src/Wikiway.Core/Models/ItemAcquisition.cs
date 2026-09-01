namespace Wikiway.Core.Models;

public sealed record ItemAcquisition(
    IReadOnlyList<RecipeSource> Recipes,
    IReadOnlyList<VendorSource> Vendors,
    IReadOnlyList<ExchangeSource> Exchanges,
    IReadOnlyList<GatheringSource> Gathering,
    IReadOnlyList<FishingSource> Fishing,
    IReadOnlyList<SealVendorSource> SealVendors,
    IReadOnlyList<VentureSource> Ventures,
    string FishingNote);

public sealed record ItemAmount(string Name, uint Amount);

public sealed record RecipeSource(
    string CraftType, ushort Level, IReadOnlyList<ItemAmount> Ingredients, string MasterBook);

public sealed record VendorSource(string NpcName, MapLocation? Location, uint GilPrice);

// Each offer is one purchase option; all parts of an offer are paid together.
public sealed record ExchangeSource(
    string ShopName, string NpcName, MapLocation? Location, IReadOnlyList<IReadOnlyList<ItemAmount>> Costs);

public sealed record GatheringSource(string NodeType, int Level, MapLocation? Location, string TimeWindow);

public sealed record FishingSource(string SpotName, int Level, MapLocation? Location, bool Spearfishing);

// RequiredRank is "" for rank 1: any company member can buy those.
public sealed record SealVendorSource(string NpcName, MapLocation? Location, uint SealCost, string RequiredRank);

// Quantities is the yield band across retainer stats, e.g. "15-50".
public sealed record VentureSource(string Category, int Level, uint VentureCost, string Quantities);
