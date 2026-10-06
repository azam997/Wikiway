using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Core.Providers;
using Xunit;

namespace Wikiway.Canary.Tests;

// End-to-end searches through LocalGameDataProvider: ranking and phrasing.
public partial class GameDataCanaryTests
{
    // The "cosmic" family is ~230 items at 7.x, all landing in one score
    // tier where ties break shortest-name-first - a cap below the family
    // size silently starves its long-named members (Operator's attire).
    [Fact]
    public async Task CosmicItemFamilyComesBackWhole()
    {
        var provider = new LocalGameDataProvider(fixture.Store());

        var result = await provider.SearchAsync(
            new NormalizedQuery("cosmic", "cosmic", QueryIntent.Unknown, SearchCategory.Items),
            CancellationToken.None);

        Assert.True(result.Results.Count > 150, $"only {result.Results.Count} cosmic items came back");
        Assert.Contains(result.Results,
            r => r.Title.Equals("Cosmic Operator's Attire", StringComparison.OrdinalIgnoreCase));
    }

    // With no prefix to filter kinds, phrasing alone has to pick the card: an
    // NPC named exactly "Gold Saucer" ties the curated Gold Saucer unlockable
    // on name score at 7.x.
    [Fact]
    public async Task UnlockPhrasingPicksTheGoldSaucerOverTheSameNamedNpc()
    {
        var normalizer = new QueryNormalizer();
        var query = normalizer.Normalize("how do i unlock the gold saucer");
        var local = await new LocalGameDataProvider(fixture.Store()).SearchAsync(query, CancellationToken.None);

        var top = new ResultRanker().Merge(query, [local]).OfType<EntityCardResult>().First();

        Assert.IsType<DutyEntity>(top.Entity);
        Assert.Contains(local.Results, r => r is EntityCardResult { Entity: NpcEntity, Title: "Gold Saucer" });
    }
}
