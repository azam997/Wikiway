using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

public interface IGameDataStore
{
    IReadOnlyList<NameIndexEntry> GetAllNames(CancellationToken ct = default);

    ItemEntity? GetItem(uint rowId);
    NpcEntity? GetNpc(uint rowId);
    QuestEntity? GetQuest(uint rowId);
    MountEntity? GetMount(uint rowId);
    MinionEntity? GetMinion(uint rowId);
    AchievementEntity? GetAchievement(uint rowId);
    DutyEntity? GetDuty(uint rowId);
    OrchestrionEntity? GetOrchestrion(uint rowId);
    TripleTriadCardEntity? GetTripleTriadCard(uint rowId);
    EmoteEntity? GetEmote(uint rowId);
    VistaEntity? GetVista(uint rowId);
    HuntMarkEntity? GetHuntMark(uint rowId);
    AetherCurrentZoneEntity? GetAetherCurrentZone(uint rowId);
    FateEntity? GetFate(uint rowId);
    LeveEntity? GetLeve(uint rowId);

    DutyEntity? FindDutyByTerritory(uint territoryTypeId);

    // Name-only reads for game-thread callbacks (context menu, territory
    // change): they must never trigger the heavy lazy index builds above.
    string? GetItemName(uint rowId);
    string? GetNpcName(uint rowId);
    string? FindSoloDutyName(uint territoryTypeId);
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
    Gatherable,
    Orchestrion,
    TripleTriadCard,
    Emote,
    Vista,
    HuntMark,
    AetherCurrentZone,
    Fate,
    Leve,
}

public sealed record NameIndexEntry(EntityKind Kind, uint RowId, string Name);
