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
    : GameEntity(RowId, Name);

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
    public IReadOnlyList<QuestChainStep> UnlockChain { get; init; } = [];
    public string? MsqRequirement { get; init; }
    public bool ChainContinues { get; init; }
}

public sealed record QuestLink(uint RowId, string Name);

public enum QuestJoin
{
    None,
    All,
    Any,
}

public sealed record QuestChainStep(
    QuestLink Quest,
    int Depth,
    QuestJoin Join,
    ushort Level,
    MapLocation? StartLocation = null,
    string? MsqVersion = null);

public sealed record MountEntity(uint RowId, string Name, ushort Icon = 0) : GameEntity(RowId, Name);

public sealed record MinionEntity(uint RowId, string Name, ushort Icon = 0) : GameEntity(RowId, Name);

public sealed record AchievementEntity(uint RowId, string Name, string Description, string Category)
    : GameEntity(RowId, Name);

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

public sealed record MapLocation(uint TerritoryTypeId, uint MapId, float MapX, float MapY, string ZoneName);
