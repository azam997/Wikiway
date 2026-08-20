using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

public interface IGameDataStore
{
    IReadOnlyList<NameIndexEntry> GetAllNames();

    ItemEntity? GetItem(uint rowId);
    NpcEntity? GetNpc(uint rowId);
    QuestEntity? GetQuest(uint rowId);
    MountEntity? GetMount(uint rowId);
    MinionEntity? GetMinion(uint rowId);
    AchievementEntity? GetAchievement(uint rowId);
    DutyEntity? GetDuty(uint rowId);

    DutyEntity? FindDutyByTerritory(uint territoryTypeId);
}

public enum EntityKind
{
    Item,
    Npc,
    Quest,
    Mount,
    Minion,
    Achievement,
    Duty,
    Unlockable,
}

public sealed record NameIndexEntry(EntityKind Kind, uint RowId, string Name);
