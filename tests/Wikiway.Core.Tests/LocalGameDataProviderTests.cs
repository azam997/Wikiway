using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Providers;
using Xunit;

namespace Wikiway.Core.Tests;

public class LocalGameDataProviderTests
{
    [Fact]
    public async Task DuplicateNamedNpcsCollapseToThePrimaryPlacement()
    {
        var store = new StubStore(
            new NpcEntity(1, "Momodi", new MapLocation(1, 10, 11.7f, 9.7f, "Ul'dah - Steps of Nald"), 31),
            new NpcEntity(2, "Momodi", new MapLocation(2, 11, 12.1f, 13.5f, "New Gridania"), 1),
            new NpcEntity(3, "Momodi", null));

        var provider = new LocalGameDataProvider(store);
        var result = await provider.SearchAsync(
            new NormalizedQuery("momodi", "momodi", QueryIntent.Unknown), CancellationToken.None);

        var card = Assert.IsType<EntityCardResult>(Assert.Single(result.Results));
        Assert.Equal(3, card.MergedCount);
        Assert.Equal("Ul'dah - Steps of Nald", Assert.Single(card.MergedLocations).ZoneName);
        Assert.Equal(2, card.MergedHidden);
    }

    [Fact]
    public async Task UnlocksCategoryReturnsQuestsAndUnlockables()
    {
        var store = new StubStore(
            new QuestEntity(1, "The Navel", 20, "Main Scenario", []),
            new DutyEntity(2, "The Navel", "Trials", 20, 0, false, false, 1) { Optional = true },
            new NpcEntity(3, "The Navel Keeper", null));

        var provider = new LocalGameDataProvider(store);
        var result = await provider.SearchAsync(
            new NormalizedQuery("the navel", "the navel", QueryIntent.Unlock, SearchCategory.Unlocks),
            CancellationToken.None);

        var entities = result.Results.OfType<EntityCardResult>().Select(c => c.Entity).ToList();
        Assert.Contains(entities, e => e is QuestEntity);
        Assert.Contains(entities, e => e is DutyEntity);
        Assert.DoesNotContain(entities, e => e is NpcEntity);
    }

    [Fact]
    public async Task WideMatchesCapAtTheRunawayBound()
    {
        var npcs = Enumerable.Range(1, 510)
            .Select(i => (GameEntity)new NpcEntity((uint)i, $"Cosmic Wanderer {i:000}", null))
            .ToArray();
        var store = new StubStore(npcs);

        var provider = new LocalGameDataProvider(store);
        var result = await provider.SearchAsync(
            new NormalizedQuery("cosmic", "cosmic", QueryIntent.Unknown), CancellationToken.None);

        Assert.Equal(500, result.Results.Count);
    }

    private sealed class StubStore(params GameEntity[] entities) : IGameDataStore
    {
        public IReadOnlyList<NameIndexEntry> GetAllNames(CancellationToken ct = default) =>
            entities.Select(e => new NameIndexEntry(KindOf(e), e.RowId, e.Name.ToLowerInvariant())).ToList();

        public NpcEntity? GetNpc(uint rowId) => Find<NpcEntity>(rowId);
        public QuestEntity? GetQuest(uint rowId) => Find<QuestEntity>(rowId);
        public DutyEntity? GetDuty(uint rowId) => Find<DutyEntity>(rowId);
        public ItemEntity? GetItem(uint rowId) => Find<ItemEntity>(rowId);
        public MountEntity? GetMount(uint rowId) => null;
        public MinionEntity? GetMinion(uint rowId) => null;
        public AchievementEntity? GetAchievement(uint rowId) => null;
        public DutyEntity? FindDutyByTerritory(uint territoryTypeId) => null;
        public string? GetItemName(uint rowId) => Find<ItemEntity>(rowId)?.Name;
        public string? GetNpcName(uint rowId) => Find<NpcEntity>(rowId)?.Name;
        public string? FindSoloDutyName(uint territoryTypeId) => null;

        private T? Find<T>(uint rowId) where T : GameEntity =>
            entities.OfType<T>().FirstOrDefault(e => e.RowId == rowId);

        private static EntityKind KindOf(GameEntity e) => e switch
        {
            NpcEntity => EntityKind.Npc,
            QuestEntity => EntityKind.Quest,
            DutyEntity => EntityKind.Unlockable,
            _ => EntityKind.Item,
        };
    }
}
