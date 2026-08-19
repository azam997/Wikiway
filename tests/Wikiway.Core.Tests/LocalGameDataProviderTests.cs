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

    private sealed class StubStore(params NpcEntity[] npcs) : IGameDataStore
    {
        public IReadOnlyList<NameIndexEntry> GetAllNames() =>
            npcs.Select(n => new NameIndexEntry(EntityKind.Npc, n.RowId, n.Name.ToLowerInvariant())).ToList();

        public NpcEntity? GetNpc(uint rowId) => npcs.FirstOrDefault(n => n.RowId == rowId);

        public ItemEntity? GetItem(uint rowId) => null;
        public QuestEntity? GetQuest(uint rowId) => null;
        public MountEntity? GetMount(uint rowId) => null;
        public MinionEntity? GetMinion(uint rowId) => null;
        public AchievementEntity? GetAchievement(uint rowId) => null;
        public DutyEntity? GetDuty(uint rowId) => null;
        public DutyEntity? FindDutyByTerritory(uint territoryTypeId) => null;
    }
}
