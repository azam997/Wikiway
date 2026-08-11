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

    public static WikiPageResult Wiki(string title, double score, string? snippet = null) => new()
    {
        Title = title,
        Source = new Citation("consolegameswiki", new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title))),
        PageUrl = new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title)),
        Snippet = snippet,
        Score = score,
    };

    public static NpcEntity Npc(string name = "Momodi") => new(1, name, null);

    public static ItemEntity Item(string name = "Iron Ingot") => new(2, name, "Metal", "An ingot of smelted iron.");
}
