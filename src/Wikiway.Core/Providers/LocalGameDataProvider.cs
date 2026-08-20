using Wikiway.Core.Abstractions;
using Wikiway.Core.Matching;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;

namespace Wikiway.Core.Providers;

public sealed class LocalGameDataProvider : ISearchProvider
{
    public const string ProviderId = "local-gamedata";

    private static readonly Citation GameDataSource = new("Game data");

    private readonly IGameDataStore store;
    private readonly Func<uint, bool>? sceneQuestVisible;
    private readonly Task<FuzzyNameIndex> indexTask;

    public LocalGameDataProvider(IGameDataStore store, Func<uint, bool>? sceneQuestVisible = null)
    {
        this.store = store;
        this.sceneQuestVisible = sceneQuestVisible;
        // Building the index walks every sheet, so it starts warming immediately
        // rather than stalling the first query.
        indexTask = Task.Run(() => new FuzzyNameIndex(store.GetAllNames()));
    }

    public string Id => ProviderId;

    public bool IsAvailable => true;

    public async Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct)
    {
        var index = await indexTask.WaitAsync(ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var match in index.Search(query.Term, kinds: KindFilter(query.Category)))
        {
            ct.ThrowIfCancellationRequested();

            var entity = Resolve(match.Entry);
            if (entity == null)
                continue;

            results.Add(new EntityCardResult
            {
                Title = entity.Name,
                Source = GameDataSource,
                Entity = entity,
                Score = match.Score,
            });
        }

        return new ProviderResult(Id, EntityGrouper.Collapse(results, sceneQuestVisible), ProviderStatus.Ok);
    }

    // Items still cover gatherables - ore is an item whether or not a node
    // spawns it. Gathering is the narrower lens; duties have no lens of their
    // own anymore and ride the unfiltered Other search.
    private static EntityKind[]? KindFilter(SearchCategory category) => category switch
    {
        SearchCategory.Items => [EntityKind.Item, EntityKind.Gatherable],
        SearchCategory.Npcs => [EntityKind.Npc],
        SearchCategory.Gathering => [EntityKind.Gatherable],
        SearchCategory.Unlocks => [EntityKind.Quest, EntityKind.Unlockable],
        _ => null,
    };

    private GameEntity? Resolve(NameIndexEntry entry) => entry.Kind switch
    {
        EntityKind.Item or EntityKind.Gatherable => store.GetItem(entry.RowId),
        EntityKind.Npc => store.GetNpc(entry.RowId),
        EntityKind.Quest => store.GetQuest(entry.RowId),
        EntityKind.Mount => store.GetMount(entry.RowId),
        EntityKind.Minion => store.GetMinion(entry.RowId),
        EntityKind.Achievement => store.GetAchievement(entry.RowId),
        EntityKind.Duty or EntityKind.Unlockable => store.GetDuty(entry.RowId),
        _ => null,
    };
}
