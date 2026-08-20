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

    public static NpcEntity Npc(string name, MapLocation? location, uint rowId = 1, int handlers = 0,
        IReadOnlyList<CutsceneAppearance>? sceneQuests = null) =>
        new(rowId, name, location, handlers) { SceneQuests = sceneQuests ?? [] };

    public static CutsceneAppearance Scene(uint questId, string questName, string expansion = "A Realm Reborn",
        int order = 0) =>
        new(new QuestLink(questId, questName), expansion, order);

    public static MapLocation Loc(string zone, float x, float y, uint mapId = 10) => new(1, mapId, x, y, zone);

    public static ItemEntity Item(string name = "Iron Ingot") => new(2, name, "Metal", "An ingot of smelted iron.", true);

    public static QuestEntity Quest(string name = "The Ultimate Weapon") => new(3, name, 50, "Main Scenario", []);

    public static DutyEntity Duty(string name = "The Navel", bool optional = false) =>
        new(4, name, "Trials", 20, 0, false, false, 1) { Optional = optional };

    public static WikiSectionsResult Sections(string title, params WikiSectionText[] sections) => new()
    {
        Title = title,
        Source = new Citation("consolegameswiki"),
        PageUrl = new Uri("https://ffxiv.consolegameswiki.com/wiki/" + Uri.EscapeDataString(title)),
        Sections = sections,
        Score = 0.95,
    };
}
