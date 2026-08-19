namespace Wikiway.Core.Models;

public abstract record GameEntity(uint RowId, string Name);

public sealed record ItemEntity(uint RowId, string Name, string Category, string Description, bool Marketable)
    : GameEntity(RowId, Name);

public sealed record NpcEntity(uint RowId, string Name, MapLocation? Location)
    : GameEntity(RowId, Name);

public sealed record QuestEntity(
    uint RowId,
    string Name,
    ushort ClassJobLevel,
    string Genre,
    IReadOnlyList<QuestLink> Prerequisites)
    : GameEntity(RowId, Name);

public sealed record QuestLink(uint RowId, string Name);

public sealed record MountEntity(uint RowId, string Name) : GameEntity(RowId, Name);

public sealed record MinionEntity(uint RowId, string Name) : GameEntity(RowId, Name);

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
    : GameEntity(RowId, Name);

public sealed record MapLocation(uint TerritoryTypeId, uint MapId, float MapX, float MapY, string ZoneName);
