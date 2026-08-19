namespace Wikiway.Core.Models;

public sealed record ItemAcquisition(
    IReadOnlyList<RecipeSource> Recipes,
    IReadOnlyList<VendorSource> Vendors);

public sealed record RecipeSource(string CraftType, ushort Level, IReadOnlyList<string> Ingredients);

public sealed record VendorSource(string NpcName, MapLocation? Location, uint GilPrice);
