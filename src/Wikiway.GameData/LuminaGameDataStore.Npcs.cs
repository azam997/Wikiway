using Lumina.Excel.Sheets;
using Wikiway.Core.Models;

namespace Wikiway.GameData;

// NPCs: the card, the event-handler data EntityGrouper uses to tell real
// placements from quest-scene copies, and the NPC -> map position index.
public sealed partial class LuminaGameDataStore
{
    private const byte LevelObjectTypeEventNpc = 8;

    // One ENpcResident row. Same-named copies are merged later by EntityGrouper
    // (Core), which uses the handler count and scene quests to tell real homes
    // from cutscene stand-ins.
    public NpcEntity? GetNpc(uint rowId)
    {
        var row = Sheet<ENpcResident>().GetRowOrDefault(rowId);
        if (row == null || row.Value.Singular.IsEmpty)
            return null;

        var (handlers, sceneQuests) = ReadEventHandlers(rowId);
        return new NpcEntity(rowId, TitleCase(row.Value.Singular.ExtractText()), FindLocation(rowId), handlers)
        {
            SceneQuests = sceneQuests,
        };
    }

    // Counts the NPC's event handlers (ENpcBase.ENpcData) and lists the quests
    // among them.
    private (int Count, IReadOnlyList<CutsceneAppearance> SceneQuests) ReadEventHandlers(uint npcRowId)
    {
        var row = Sheet<ENpcBase>().GetRowOrDefault(npcRowId);
        if (row == null)
            return (0, []);

        var count = 0;
        List<CutsceneAppearance>? quests = null;
        foreach (var handler in row.Value.ENpcData)
        {
            if (handler.RowId == 0)
                continue;
            count++;

            // Quest handler ids live in the 0x10000 block and double as Quest row ids.
            if (handler.RowId is < 0x10000 or >= 0x20000)
                continue;

            var quest = Sheet<Quest>().GetRowOrDefault(handler.RowId);
            if (quest == null || quest.Value.Name.IsEmpty)
                continue;

            (quests ??= []).Add(new CutsceneAppearance(
                new QuestLink(handler.RowId, quest.Value.Name.ExtractText()),
                quest.Value.Expansion.ValueNullable?.Name.ExtractText() ?? "",
                (int)quest.Value.Expansion.RowId));
        }

        return (count, quests ?? (IReadOnlyList<CutsceneAppearance>)[]);
    }

    // Where the NPC stands, from its Level row; null when no Level row places
    // it (e.g. housing-district vendor copies).
    private MapLocation? FindLocation(uint npcRowId) =>
        npcLevels.Value.TryGetValue(npcRowId, out var level) ? ToMapLocation(level) : null;

    // ENpcResident -> its Level placement (object type 8 = event NPC).
    private Dictionary<uint, Level> BuildNpcLevelLookup()
    {
        // A full Level scan is ~100k rows; do it once and keep the ENpc entries.
        var lookup = new Dictionary<uint, Level>();
        foreach (var level in Sheet<Level>())
        {
            if (level.Type == LevelObjectTypeEventNpc)
                lookup.TryAdd(level.Object.RowId, level);
        }

        return lookup;
    }
}
