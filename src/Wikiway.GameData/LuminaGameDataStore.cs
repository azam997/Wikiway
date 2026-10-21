using Lumina.Excel;
using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// Every read of the game's Excel sheets goes through this class, which turns
// sheet rows into the plain Core entity records (ItemEntity, QuestEntity...).
// It never touches Dalamud: the plugin hands it Lumina's GameData, and the
// canary tests construct it straight from a game install.
//
// The class is split by feature: .Names (the search index), .Items,
// .Acquisition + .Shops, .Usage, .Collections, .Quests, .Duties, .Npcs,
// .World (vistas, hunts, currents, FATEs, leves, achievements) and
// .Locations (map coordinates, nearest aetheryte).
//
// Sheet facts that aren't obvious from the column names were checked against
// the real sheets with a test query/probe first; the comments indicate where
// this was necessary, and a canary test in tests/Wikiway.Canary.Tests ensures
// this probe result keeps being checked in the future.
public sealed partial class LuminaGameDataStore : IGameDataStore
{
    private readonly Lumina.GameData gameData;

    // Most reads happen on request: a search builds one card per match from
    // forward row reads, and an item card's sources are scanned when the card
    // is opened (GetItemDetails). The two lookups below are the exception:
    // every result of their kind needs them before it can be shown, so a
    // scan per result would multiply with the match count. Each is built once
    // on first use and kept.

    // Where each NPC stands. Merging an NPC's copies (Y'shtola has 415) needs
    // every copy's position.
    private readonly LazyLookup<Dictionary<uint, Level>> npcLevels;

    // Which quest unlocks each instanced duty. The name index needs it to tell
    // duties from unlockables, and every duty card needs it for spoiler hiding.
    private readonly LazyLookup<Dictionary<uint, uint>> unlockQuestByInstance;

    public LuminaGameDataStore(Lumina.GameData gameData)
    {
        this.gameData = gameData;

        npcLevels = new(BuildNpcLevelLookup);
        unlockQuestByInstance = new(BuildInstanceUnlockLookup);
    }

    // The `!` alternative turns a renamed sheet after a game patch into a bare
    // NullReferenceException deep in a background task; name the sheet instead.
    private ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T> =>
        gameData.GetExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    private SubrowExcelSheet<T> SubrowSheet<T>() where T : struct, IExcelSubrow<T> =>
        gameData.GetSubrowExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    // Appends to a one-to-many index, creating the list on first use.
    private static void Add(Dictionary<uint, List<uint>> lookup, uint key, uint value)
    {
        if (!lookup.TryGetValue(key, out var list))
            lookup[key] = list = [];
        list.Add(value);
    }

    // Singular names come lowercased ("momodi modi"); the UI wants display casing.
    private static string TitleCase(string name) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
