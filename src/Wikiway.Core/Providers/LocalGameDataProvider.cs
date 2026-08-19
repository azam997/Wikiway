using Wikiway.Core.Abstractions;
using Wikiway.Core.Matching;
using Wikiway.Core.Models;

namespace Wikiway.Core.Providers;

public sealed class LocalGameDataProvider : ISearchProvider
{
    public const string ProviderId = "local-gamedata";

    private static readonly Citation GameDataSource = new("Game data");

    private readonly IGameDataStore store;
    private readonly Task<FuzzyNameIndex> indexTask;

    public LocalGameDataProvider(IGameDataStore store)
    {
        this.store = store;
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
        foreach (var match in index.Search(query.Term, kind: KindFilter(query.Category)))
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

        return new ProviderResult(Id, results, ProviderStatus.Ok);
    }

    private static EntityKind? KindFilter(SearchCategory category) => category switch
    {
        SearchCategory.Items => EntityKind.Item,
        SearchCategory.Quests => EntityKind.Quest,
        SearchCategory.Npcs => EntityKind.Npc,
        SearchCategory.Duties => EntityKind.Duty,
        _ => null,
    };

    private GameEntity? Resolve(NameIndexEntry entry) => entry.Kind switch
    {
        EntityKind.Item => store.GetItem(entry.RowId),
        EntityKind.Npc => store.GetNpc(entry.RowId),
        EntityKind.Quest => store.GetQuest(entry.RowId),
        EntityKind.Mount => store.GetMount(entry.RowId),
        EntityKind.Minion => store.GetMinion(entry.RowId),
        EntityKind.Achievement => store.GetAchievement(entry.RowId),
        EntityKind.Duty => store.GetDuty(entry.RowId),
        _ => null,
    };
}
