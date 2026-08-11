using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public sealed class ResultRanker
{
    public IReadOnlyList<SearchResult> Merge(NormalizedQuery query, IEnumerable<ProviderResult> providerResults)
    {
        return providerResults
            .SelectMany(pr => pr.Results)
            .OrderByDescending(r => EffectiveScore(query, r))
            .ToList();
    }

    // Local game data outranks wiki hits; an exact entity match outranks everything.
    private static double EffectiveScore(NormalizedQuery query, SearchResult result)
    {
        var score = result.Score;

        if (result is EntityCardResult card)
        {
            score += card.Score >= 0.999 ? 2.0 : 1.0;

            score += (query.Intent, card.Entity) switch
            {
                (QueryIntent.Location, NpcEntity) => 0.25,
                (QueryIntent.Unlock, QuestEntity) => 0.25,
                (QueryIntent.Acquisition, ItemEntity) => 0.25,
                (QueryIntent.Acquisition, MountEntity or MinionEntity) => 0.15,
                _ => 0,
            };
        }

        return score;
    }
}
