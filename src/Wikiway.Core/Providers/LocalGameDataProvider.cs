using Wikiway.Core.Abstractions;
using Wikiway.Core.Matching;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;

namespace Wikiway.Core.Providers;

// Answers queries from the game's own data: fuzzy-match the term against every
// name, build an entity card per match, then merge same-named duplicates
// (EntityGrouper). Needs no network.
public sealed class LocalGameDataProvider : ISearchProvider
{
    public const string ProviderId = "local-gamedata";

    // A safety cap, not a page size: real name families ("cosmic ..." is ~230
    // items at 7.x) must come back whole, because the shortest-name tie-break
    // would otherwise crowd out the longer names.
    private const int MaxResults = 500;

    private static readonly Citation GameDataSource = new("Game data");

    private readonly IGameDataStore store;
    private readonly Func<uint, bool>? sceneQuestVisible;
    private readonly Task<FuzzyNameIndex> indexTask;

    public LocalGameDataProvider(IGameDataStore store, Func<uint, bool>? sceneQuestVisible = null,
        CancellationToken lifetime = default)
    {
        this.store = store;
        this.sceneQuestVisible = sceneQuestVisible;
        // Building the index walks every sheet, so it starts warming immediately
        // rather than stalling the first query.
        indexTask = Task.Run(() => new FuzzyNameIndex(store.GetAllNames(lifetime)), lifetime);
    }

    // Lets the plugin wait for the build to stop before unloading.
    public Task IndexTask => indexTask;

    public string Id => ProviderId;

    public bool IsAvailable => true;

    public async Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct)
    {
        var index = await indexTask.WaitAsync(ct).ConfigureAwait(false);

        var results = new List<SearchResult>();
        foreach (var match in index.Search(query.Term, MaxResults, KindFilter(query.Category)))
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
    // spawns it. Gathering is the narrower filter; duties have no filter of
    // their own and are found by the unprefixed search.
    private static EntityKind[]? KindFilter(SearchCategory category) => category switch
    {
        SearchCategory.Items => [EntityKind.Item, EntityKind.Gatherable],
        SearchCategory.Npcs => [EntityKind.Npc],
        SearchCategory.Gathering => [EntityKind.Gatherable],
        SearchCategory.Unlocks => [EntityKind.Quest, EntityKind.Unlockable],
        _ => null,
    };

    // Turns an index hit into its full entity; null if the row no longer
    // resolves.
    private GameEntity? Resolve(NameIndexEntry entry) => entry.Kind switch
    {
        EntityKind.Item or EntityKind.Gatherable => store.GetItem(entry.RowId),
        EntityKind.Npc => store.GetNpc(entry.RowId),
        EntityKind.Quest => store.GetQuest(entry.RowId),
        EntityKind.Mount => store.GetMount(entry.RowId),
        EntityKind.Minion => store.GetMinion(entry.RowId),
        EntityKind.Achievement => store.GetAchievement(entry.RowId),
        EntityKind.Duty or EntityKind.Unlockable => store.GetDuty(entry.RowId),
        EntityKind.Orchestrion => store.GetOrchestrion(entry.RowId),
        EntityKind.TripleTriadCard => store.GetTripleTriadCard(entry.RowId),
        EntityKind.Emote => store.GetEmote(entry.RowId),
        EntityKind.Vista => store.GetVista(entry.RowId),
        EntityKind.HuntMark => store.GetHuntMark(entry.RowId),
        EntityKind.AetherCurrentZone => store.GetAetherCurrentZone(entry.RowId),
        EntityKind.Fate => store.GetFate(entry.RowId),
        EntityKind.Leve => store.GetLeve(entry.RowId),
        _ => null,
    };
}
