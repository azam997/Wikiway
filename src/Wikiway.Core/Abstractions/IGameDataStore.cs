using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

// Read access to the game's own data, implemented by LuminaGameDataStore. Core
// only sees these plain records, never Lumina types, which keeps Core testable
// with hand-written stubs.
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

    // Scans several whole sheets (up to ~100 ms for an item sold in every kind
    // of shop), so the UI calls it on a background task when an item card is
    // opened.
    ItemDetails GetItemDetails(uint itemRowId);

    // Light reads for game-thread callbacks (context menu, territory change):
    // a single row, or one pass over the small duty sheet.
    string? GetItemName(uint rowId);
    string? GetNpcName(uint rowId);
    string? FindSoloDutyName(uint territoryTypeId);
}

// What a searchable name refers to; picks which Get method resolves it.
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

// One searchable name. Name is lowercased; RowId is the row in the kind's
// sheet.
public sealed record NameIndexEntry(EntityKind Kind, uint RowId, string Name);
