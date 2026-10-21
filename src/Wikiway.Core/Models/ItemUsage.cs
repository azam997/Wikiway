namespace Wikiway.Core.Models;

// What an item is used for. The materia stat lives on ItemEntity, because the
// collapsed row shows it.
public sealed record ItemUsage(
    int UsedInRecipes,
    IReadOnlyList<DeliverySource> Deliveries,
    IReadOnlyList<CollectableTurnIn> CollectableTurnIns,
    TreasureMapInfo? TreasureMap);

public sealed record DeliverySource(string NpcName, QuestLink? UnlockQuest);

// MaxScrips is the top-quality payout; the scrip type is not named anywhere
// in the sheets (CollectablesShopRewardScrip.Currency is an opaque id), so
// the UI says "scrips" and the wiki covers the color.
public sealed record CollectableTurnIn(int LevelMin, int LevelMax, int MaxScrips);

public sealed record TreasureMapInfo(int PartySize, IReadOnlyList<TreasureZone> Zones);

public sealed record TreasureZone(string ZoneName, int SpotCount);
