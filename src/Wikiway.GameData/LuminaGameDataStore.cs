using Lumina.Excel.Sheets;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

public sealed class LuminaGameDataStore : IGameDataStore
{
    private const byte LevelObjectTypeEventNpc = 8;

    private readonly Lumina.GameData gameData;
    private readonly Lock levelLock = new();
    private Dictionary<uint, Level>? npcLevels;

    public LuminaGameDataStore(Lumina.GameData gameData)
    {
        this.gameData = gameData;
    }

    public IReadOnlyList<NameIndexEntry> GetAllNames()
    {
        var names = new List<NameIndexEntry>(80_000);

        foreach (var row in gameData.GetExcelSheet<Item>()!)
            AddName(names, EntityKind.Item, row.RowId, row.Name.ExtractText());

        foreach (var row in gameData.GetExcelSheet<ENpcResident>()!)
            AddName(names, EntityKind.Npc, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Quest>()!)
            AddName(names, EntityKind.Quest, row.RowId, row.Name.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Mount>()!)
            AddName(names, EntityKind.Mount, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Companion>()!)
            AddName(names, EntityKind.Minion, row.RowId, row.Singular.ExtractText());

        foreach (var row in gameData.GetExcelSheet<Achievement>()!)
            AddName(names, EntityKind.Achievement, row.RowId, row.Name.ExtractText());

        return names;
    }

    private static void AddName(List<NameIndexEntry> names, EntityKind kind, uint rowId, string name)
    {
        if (name.Length > 0)
            names.Add(new NameIndexEntry(kind, rowId, name.ToLowerInvariant()));
    }

    public ItemEntity? GetItem(uint rowId)
    {
        var row = gameData.GetExcelSheet<Item>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new ItemEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.ItemUICategory.ValueNullable?.Name.ExtractText() ?? "",
            row.Value.Description.ExtractText());
    }

    public NpcEntity? GetNpc(uint rowId)
    {
        var row = gameData.GetExcelSheet<ENpcResident>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new NpcEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), FindLocation(rowId));
    }

    public QuestEntity? GetQuest(uint rowId)
    {
        var row = gameData.GetExcelSheet<Quest>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        var quest = row.Value;
        var prerequisites = new List<QuestLink>();
        foreach (var previous in quest.PreviousQuest)
        {
            if (previous.RowId != 0 && previous.ValueNullable is { } prev && !prev.Name.IsEmpty)
                prerequisites.Add(new QuestLink(previous.RowId, prev.Name.ExtractText()));
        }

        return new QuestEntity(
            rowId,
            quest.Name.ExtractText(),
            quest.ClassJobLevel.FirstOrDefault(),
            quest.JournalGenre.ValueNullable?.Name.ExtractText() ?? "",
            prerequisites);
    }

    public MountEntity? GetMount(uint rowId)
    {
        var row = gameData.GetExcelSheet<Mount>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MountEntity(rowId, TitleCase(row.Value.Singular.ExtractText()));
    }

    public MinionEntity? GetMinion(uint rowId)
    {
        var row = gameData.GetExcelSheet<Companion>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        return new MinionEntity(rowId, TitleCase(row.Value.Singular.ExtractText()));
    }

    public AchievementEntity? GetAchievement(uint rowId)
    {
        var row = gameData.GetExcelSheet<Achievement>()!.GetRowOrDefault(rowId);
        if (row == null || row.Value.Name.IsEmpty)
            return null;

        return new AchievementEntity(
            rowId,
            row.Value.Name.ExtractText(),
            row.Value.Description.ExtractText(),
            row.Value.AchievementCategory.ValueNullable?.Name.ExtractText() ?? "");
    }

    private MapLocation? FindLocation(uint npcRowId)
    {
        var levels = npcLevels;
        if (levels == null)
        {
            lock (levelLock)
            {
                levels = npcLevels ??= BuildNpcLevelLookup();
            }
        }

        if (!levels.TryGetValue(npcRowId, out var level))
            return null;

        var map = level.Map.ValueNullable;
        var territory = level.Territory.ValueNullable;
        if (map == null || territory == null)
            return null;

        var zone = territory.Value.PlaceName.ValueNullable?.Name.ExtractText() ?? "";

        // World Z is the north-south axis; map Y comes from it, not from world Y (height).
        var mapX = MapCoordConverter.ToMapCoord(level.X, map.Value.SizeFactor, map.Value.OffsetX);
        var mapY = MapCoordConverter.ToMapCoord(level.Z, map.Value.SizeFactor, map.Value.OffsetY);

        return new MapLocation(territory.Value.RowId, map.Value.RowId, mapX, mapY, zone);
    }

    private Dictionary<uint, Level> BuildNpcLevelLookup()
    {
        // A full Level scan is ~100k rows; do it once and keep the ENpc entries.
        var lookup = new Dictionary<uint, Level>();
        foreach (var level in gameData.GetExcelSheet<Level>()!)
        {
            if (level.Type == LevelObjectTypeEventNpc)
                lookup.TryAdd(level.Object.RowId, level);
        }

        return lookup;
    }

    // Singular names come lowercased ("momodi modi"); the UI wants display casing.
    private static string TitleCase(string name) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
