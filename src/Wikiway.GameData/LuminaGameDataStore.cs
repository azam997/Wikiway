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
// .Acquisition + .AcquisitionIndex, .Usage, .Collections, .Quests, .Duties,
// .Npcs, .World (vistas, hunts, currents, FATEs, leves, achievements) and
// .Locations (map coordinates, nearest aetheryte).
//
// Sheet facts that aren't obvious from the column names were checked against
// the real sheets with a test query/probe first; the comments indicate where
// this was necessary, and a canary test in tests/Wikiway.Canary.Tests ensures
// this probe result keeps being checked in the future.
public sealed partial class LuminaGameDataStore : IGameDataStore
{
    private readonly Lumina.GameData gameData;

    // Reverse indexes ("which recipes make item X") need a full scan of one
    // or more sheets, so each is built once on first use, or ahead of time by
    // WarmAll on a background task, then kept for the plugin's lifetime.
    // The builders live in the feature files.
    private readonly LazyLookup<Dictionary<uint, List<uint>>> gatheringBases;
    private readonly LazyLookup<AcquisitionIndex> acquisitionIndex;
    private readonly LazyLookup<UsageIndex> usageIndex;
    private readonly LazyLookup<ItemActionTeachers> teachers;
    private readonly LazyLookup<Dictionary<uint, uint>> unlockQuestByInstance;
    private readonly LazyLookup<Dictionary<uint, Level>> npcLevels;
    private readonly LazyLookup<Dictionary<uint, uint>> dutyByTerritory;
    private readonly LazyLookup<Dictionary<uint, string>> markZoneByBNpcName;
    private readonly LazyLookup<Dictionary<uint, List<AetheryteMarker>>> aetherytesByMap;

    public LuminaGameDataStore(Lumina.GameData gameData)
    {
        this.gameData = gameData;

        gatheringBases = new(BuildGatheringItemLookup);
        acquisitionIndex = new(BuildAcquisitionIndex);
        usageIndex = new(BuildUsageIndex);
        teachers = new(BuildTeacherLookup);
        unlockQuestByInstance = new(BuildInstanceUnlockLookup);
        npcLevels = new(BuildNpcLevelLookup);
        dutyByTerritory = new(BuildDutyTerritoryLookup);
        markZoneByBNpcName = new(BuildMarkZoneLookup);
        aetherytesByMap = new(BuildAetheryteMarkers);
    }

    // The `!` alternative turns a renamed sheet after a game patch into a bare
    // NullReferenceException deep in a background task; name the sheet instead.
    private ExcelSheet<T> Sheet<T>() where T : struct, IExcelRow<T> =>
        gameData.GetExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    private SubrowExcelSheet<T> SubrowSheet<T>() where T : struct, IExcelSubrow<T> =>
        gameData.GetSubrowExcelSheet<T>()
        ?? throw new InvalidOperationException($"{typeof(T).Name} sheet missing - game patch?");

    // Builds every lazy index up front (Plugin starts this on a background
    // task at load) so no lookup ever pays for a sheet scan mid-search or on
    // the game thread.
    public void WarmAll(CancellationToken ct)
    {
        _ = acquisitionIndex.Value;
        _ = teachers.Value;
        _ = usageIndex.Value;
        ct.ThrowIfCancellationRequested();
        _ = unlockQuestByInstance.Value;
        ct.ThrowIfCancellationRequested();
        _ = npcLevels.Value;
        ct.ThrowIfCancellationRequested();
        _ = dutyByTerritory.Value;
        ct.ThrowIfCancellationRequested();
        _ = markZoneByBNpcName.Value;
        ct.ThrowIfCancellationRequested();
        _ = aetherytesByMap.Value;
    }

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
