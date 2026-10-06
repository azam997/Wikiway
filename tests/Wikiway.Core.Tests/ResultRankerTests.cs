using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;
using static Wikiway.Core.Tests.TestData;

namespace Wikiway.Core.Tests;

public class ResultRankerTests
{
    private readonly ResultRanker ranker = new();

    private static NormalizedQuery Query(
        QueryIntent intent = QueryIntent.Unknown,
        SearchCategory category = SearchCategory.All) =>
        new("raw", "term", intent, category);

    [Fact]
    public void ExactLocalEntityBeatsWikiHit()
    {
        var wiki = new ProviderResult("wiki", [WikiResult("Momodi", 1.0)], ProviderStatus.Ok);
        var local = new ProviderResult("local", [Card(Npc(), 1.0)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(), [wiki, local]);

        Assert.IsType<EntityCardResult>(merged[0]);
    }

    [Fact]
    public void FuzzyLocalEntityStillBeatsWiki()
    {
        var wiki = new ProviderResult("wiki", [WikiResult("Momodi", 1.0)], ProviderStatus.Ok);
        var local = new ProviderResult("local", [Card(Npc(), 0.6)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(), [wiki, local]);

        Assert.IsType<EntityCardResult>(merged[0]);
    }

    [Fact]
    public void LocationIntentPrefersNpcOverItem()
    {
        var local = new ProviderResult("local",
            [Card(Item(), 0.8), Card(Npc(), 0.8)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(QueryIntent.Location), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<NpcEntity>(first.Entity);
    }

    [Fact]
    public void UnlockIntentPrefersQuests()
    {
        var local = new ProviderResult("local",
            [Card(Npc(), 0.8), Card(Quest(), 0.8)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(QueryIntent.Unlock), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<QuestEntity>(first.Entity);
    }

    // "how do i unlock the gold saucer" without a prefix: an NPC named
    // "Gold Saucer" ties the duty card on name score.
    [Fact]
    public void UnlockIntentPrefersDutiesOverNpcs()
    {
        var local = new ProviderResult("local",
            [Card(Npc(), 1.0), Card(Duty(), 1.0)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(QueryIntent.Unlock), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<DutyEntity>(first.Entity);
    }

    // Ties break quest-first: the quest is the actionable unlock path, and
    // the duty card links to it anyway.
    [Fact]
    public void UnlockIntentBreaksTiesQuestFirst()
    {
        var local = new ProviderResult("local",
            [Card(Duty(optional: true), 0.8), Card(Quest(), 0.8)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(QueryIntent.Unlock, SearchCategory.Unlocks), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<QuestEntity>(first.Entity);
    }

    [Fact]
    public void WikiResultsKeepRelevanceOrder()
    {
        var wiki = new ProviderResult("wiki",
            [WikiResult("Less relevant", 0.4), WikiResult("More relevant", 0.9)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(), [wiki]);

        Assert.Equal("More relevant", merged[0].Title);
    }
}
