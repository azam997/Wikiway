using Wikiway.Core.Models;

namespace Wikiway.Core.Tests;

internal static class TestData
{
    public static EntityCardResult Card(GameEntity entity, double score) => new()
    {
        Title = entity.Name,
        Source = new Citation("Game data"),
        Entity = entity,
        Score = score,
    };

    public static WikiPageResult WikiResult(string title, double score) => new()
    {
        Title = title,
        Source = new Citation("consolegameswiki", new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title))),
        PageUrl = new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title)),
        Score = score,
    };

    public static NpcEntity Npc(string name = "Momodi") => new(1, name, null);

    public static NpcEntity Npc(string name, MapLocation? location, uint rowId = 1, int handlers = 0) =>
        new(rowId, name, location, handlers);

    public static MapLocation Loc(string zone, float x, float y, uint mapId = 10) => new(1, mapId, x, y, zone);

    public static ItemEntity Item(string name = "Iron Ingot") => new(2, name, "Metal", "An ingot of smelted iron.", true);

    public static WikiSectionsResult Sections(string title, params WikiSectionText[] sections) => new()
    {
        Title = title,
        Source = new Citation("consolegameswiki"),
        PageUrl = new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title)),
        Sections = sections,
        Score = 0.95,
    };
}
