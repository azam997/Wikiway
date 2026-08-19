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
        SearchCategory category = SearchCategory.Other) =>
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
    public void ExplicitCategoryPrefersMatchingEntity()
    {
        var local = new ProviderResult("local",
            [Card(Npc(), 0.8), Card(Item(), 0.8)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(category: SearchCategory.Items), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<ItemEntity>(first.Entity);
    }

    [Fact]
    public void ExplicitCategoryBeatsInferredIntent()
    {
        var local = new ProviderResult("local",
            [Card(Npc(), 0.8), Card(Item(), 0.8)], ProviderStatus.Ok);

        var merged = ranker.Merge(Query(QueryIntent.Location, SearchCategory.Items), [local]);

        var first = Assert.IsType<EntityCardResult>(merged[0]);
        Assert.IsType<ItemEntity>(first.Entity);
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
