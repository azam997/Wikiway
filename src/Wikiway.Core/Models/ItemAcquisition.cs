namespace Wikiway.Core.Models;

public sealed record ItemAcquisition(
    IReadOnlyList<RecipeSource> Recipes,
    IReadOnlyList<VendorSource> Vendors,
    IReadOnlyList<ExchangeSource> Exchanges,
    IReadOnlyList<GatheringSource> Gathering);

public sealed record RecipeSource(string CraftType, ushort Level, IReadOnlyList<string> Ingredients);

public sealed record VendorSource(string NpcName, MapLocation? Location, uint GilPrice);

public sealed record ExchangeSource(string ShopName, string NpcName, MapLocation? Location, IReadOnlyList<string> Costs);

public sealed record GatheringSource(string NodeType, int Level, MapLocation? Location);
