namespace Wikiway.Core.Models;

public abstract record GameEntity(uint RowId, string Name);

public sealed record ItemEntity(
    uint RowId,
    string Name,
    string Category,
    string Description,
    bool Marketable,
    ushort Icon = 0,
    ItemAcquisition? Acquisition = null)
    : GameEntity(RowId, Name)
{
    public ItemEquipment? Equipment { get; init; }
    public ItemUsage? Usage { get; init; }
    public ItemFoodEffect? Food { get; init; }
}

public sealed record NpcEntity(uint RowId, string Name, MapLocation? Location, int EventHandlers = 0)
    : GameEntity(RowId, Name)
{
    public IReadOnlyList<CutsceneAppearance> SceneQuests { get; init; } = [];
}

public sealed record CutsceneAppearance(
    QuestLink Quest,
    string Expansion,
    int ExpansionOrder,
    MapLocation? Location = null);

public sealed record QuestEntity(
    uint RowId,
    string Name,
    ushort ClassJobLevel,
    string Genre,
    IReadOnlyList<QuestLink> Prerequisites)
    : GameEntity(RowId, Name)
{
    public QuestJoin PrerequisiteJoin { get; init; } = QuestJoin.All;
    public string Expansion { get; init; } = "";
    public bool MainScenario { get; init; }
    public MapLocation? StartLocation { get; init; }
    public IReadOnlyList<QuestChain> UnlockChains { get; init; } = [];
    public string? MsqRequirement { get; init; }
}

public sealed record QuestLink(uint RowId, string Name);

public enum QuestJoin
{
    None,
    All,
    Any,
}

// One linear run of quests in play order; a prerequisite fork spawns a
// separate chain per branch. Gate is the MSQ prerequisite this chain runs
// into (chains with no steps carry only a gate).
public sealed record QuestChain(
    IReadOnlyList<QuestChainStep> Steps,
    MsqGate? Gate = null,
    string Genre = "",
    QuestJoin Join = QuestJoin.All,
    bool Continues = false);

public sealed record QuestChainStep(
    QuestLink Quest,
    ushort Level,
    MapLocation? StartLocation = null);

public sealed record MountEntity(uint RowId, string Name, ushort Icon = 0) : GameEntity(RowId, Name)
{
    public string Description { get; init; } = "";
    public ItemEntity? TeachingItem { get; init; }
}

public sealed record MinionEntity(uint RowId, string Name, ushort Icon = 0) : GameEntity(RowId, Name)
{
    public string Description { get; init; } = "";
    public MinionBattleStats? BattleStats { get; init; }
    public ItemEntity? TeachingItem { get; init; }
}

// Lord of Verminion numbers; every summonable minion carries them.
public sealed record MinionBattleStats(
    int Hp, int Attack, int Defense, int Speed, int Cost, string SpecialAction);

public sealed record OrchestrionEntity(uint RowId, string Name, string Description, string Category)
    : GameEntity(RowId, Name)
{
    public ItemEntity? TeachingItem { get; init; }
}

public sealed record TripleTriadCardEntity(
    uint RowId,
    string Name,
    string Description,
    int Top,
    int Bottom,
    int Left,
    int Right,
    int Stars,
    string CardType,
    uint SaleValue)
    : GameEntity(RowId, Name)
{
    // The sheets name a source only for some acquisition types (NPC wins and
    // duty drops); the rest is the obtain label or nothing - the wiki fills those.
    public string ObtainText { get; init; } = "";
    public string NpcName { get; init; } = "";
    public MapLocation? NpcLocation { get; init; }
    public string DutyName { get; init; } = "";
    public ItemEntity? TeachingItem { get; init; }
}

public sealed record EmoteEntity(uint RowId, string Name, string Command, string Category)
    : GameEntity(RowId, Name)
{
    public QuestLink? UnlockQuest { get; init; }
    public ItemEntity? TeachingItem { get; init; }
}

public sealed record AchievementEntity(uint RowId, string Name, string Description, string Category)
    : GameEntity(RowId, Name);

// Hint is the sightseeing log's riddle text; Lore the location blurb.
public sealed record VistaEntity(uint RowId, string Name, string Hint, string Lore)
    : GameEntity(RowId, Name)
{
    public MapLocation? Location { get; init; }
    public string Region { get; init; } = "";
    public string Emote { get; init; } = "";

    // Eorzea-time window ("8:00-12:00 ET"); empty means any time.
    public string TimeWindow { get; init; } = "";
}

public sealed record HuntMarkEntity(uint RowId, string Name, string Rank)
    : GameEntity(RowId, Name)
{
    // Only the hunt-bill sheet names a zone (B ranks); spawn points and the
    // remaining ranks are wiki territory.
    public string ZoneName { get; init; } = "";
}

// One card per flying zone: the quests that grant currents. Field currents
// have no sheet coords (LGB) and the per-zone sets undercount them, so the
// wiki covers those.
public sealed record AetherCurrentZoneEntity(
    uint RowId, string Name, IReadOnlyList<QuestChainStep> QuestCurrents)
    : GameEntity(RowId, Name);

public sealed record FateEntity(uint RowId, string Name, byte Level, string Description)
    : GameEntity(RowId, Name)
{
    public QuestLink? RequiredQuest { get; init; }
}

public sealed record LeveEntity(
    uint RowId,
    string Name,
    ushort Level,
    string Type,
    string JobCategory,
    string Description)
    : GameEntity(RowId, Name)
{
    public MapLocation? Levemete { get; init; }
    public string IssuedAt { get; init; } = "";
    public byte AllowanceCost { get; init; }
    public uint ExpReward { get; init; }
    public uint GilReward { get; init; }
}

public sealed record DutyEntity(
    uint RowId,
    string Name,
    string ContentType,
    ushort ClassJobLevel,
    ushort ItemLevel,
    bool Solo,
    bool HighEnd,
    uint TerritoryTypeId)
    : GameEntity(RowId, Name)
{
    public QuestLink? UnlockQuest { get; init; }
    public QuestLink? ChainStart { get; init; }
    public MsqGate? MsqGate { get; init; }
    public bool FieldArea { get; init; }

    // Gated behind a quest outside the main scenario - content you can miss.
    public bool Optional { get; init; }
}

// The most advanced main-scenario quest a duty's unlock chain passes through.
public sealed record MsqGate(QuestLink Quest, string Version);

public sealed record MapLocation(
    uint TerritoryTypeId,
    uint MapId,
    float MapX,
    float MapY,
    string ZoneName,
    NearestAetheryte? Aetheryte = null);

// The closest attunable point on the location's map: a teleport aetheryte or,
// inside cities, an aethernet shard. TeleportRowId is always a real aetheryte
// (the zone's own when the nearest point is a shard); 0 when none is known.
public sealed record NearestAetheryte(string Name, bool Aethernet, uint TeleportRowId, string TeleportName);
