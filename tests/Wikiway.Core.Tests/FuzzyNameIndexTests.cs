using Wikiway.Core.Abstractions;
using Wikiway.Core.Matching;
using Xunit;

namespace Wikiway.Core.Tests;

public class FuzzyNameIndexTests
{
    private static FuzzyNameIndex Index(params string[] names) =>
        new(names.Select((n, i) => new NameIndexEntry(EntityKind.Npc, (uint)(i + 1), n)));

    [Fact]
    public void ExactMatchScoresHighest()
    {
        var index = Index("iron ingot", "iron ingot of the luminary");

        var matches = index.Search("iron ingot");

        Assert.Equal(2, matches.Count);
        Assert.Equal("iron ingot", matches[0].Entry.Name);
        Assert.Equal(1.0, matches[0].Score);
        Assert.True(matches[1].Score < 1.0);
    }

    [Fact]
    public void PrefixMatchesWhenNoExactHit()
    {
        var index = Index("momodi modi");

        var matches = index.Search("momodi");

        Assert.Single(matches);
        Assert.Equal("momodi modi", matches[0].Entry.Name);
    }

    [Fact]
    public void SubstringMatchesMidName()
    {
        var index = Index("the ultimate weapon");

        var matches = index.Search("ultimate");

        Assert.Single(matches);
    }

    [Fact]
    public void SmallTypoStillMatches()
    {
        var index = Index("momodi modi");

        var matches = index.Search("momodi mudi");

        Assert.Single(matches);
        Assert.True(matches[0].Score < 0.7);
    }

    [Fact]
    public void UnrelatedTermReturnsNothing()
    {
        var index = Index("momodi modi");

        Assert.Empty(index.Search("chocobo"));
    }

    [Fact]
    public void LimitIsRespected()
    {
        var index = Index(Enumerable.Range(0, 50).Select(i => $"item {i}").ToArray());

        Assert.Equal(8, index.Search("item", limit: 8).Count);
    }
}
