using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

// Orders the combined provider results. Provider scores are only comparable
// within a provider, so each result type gets a fixed offset on top.
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

        // Structured section answers sit below exact local cards, above generic wiki hits.
        if (result is WikiSectionsResult)
            score += 1.5;

        if (result is EntityCardResult card)
        {
            score += card.Score >= 0.999 ? 2.0 : 1.0;

            // A prefix needs no boost here: its kind filter already dropped
            // every card of another kind.
            score += (query.Intent, card.Entity) switch
            {
                (QueryIntent.Location, NpcEntity) => 0.25,
                (QueryIntent.Unlock, QuestEntity) => 0.25,
                (QueryIntent.Acquisition, ItemEntity) => 0.25,
                (QueryIntent.Acquisition,
                    MountEntity or MinionEntity or OrchestrionEntity or TripleTriadCardEntity) => 0.15,
                (QueryIntent.Location, VistaEntity or HuntMarkEntity or FateEntity) => 0.15,
                (QueryIntent.Unlock, DutyEntity or AetherCurrentZoneEntity) => 0.15,
                _ => 0,
            };
        }

        return score;
    }
}
