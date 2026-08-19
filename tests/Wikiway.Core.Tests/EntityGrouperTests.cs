using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;
using static Wikiway.Core.Tests.TestData;

namespace Wikiway.Core.Tests;

public class EntityGrouperTests
{
    [Fact]
    public void CollapsesDuplicateNpcsIntoOneCardWithDistinctLocations()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Momodi", Loc("Ul'dah - Steps of Nald", 11.7f, 9.7f), 1), 1.0),
            Card(Npc("Momodi", Loc("Ul'dah - Steps of Nald", 11.7f, 9.7f), 2), 0.9),
            Card(Npc("Momodi", Loc("New Gridania", 12.1f, 13.5f, mapId: 11), 3), 0.9),
            Card(Npc("Momodi", Loc("Limsa Lominsa Upper Decks", 10.8f, 12.1f, mapId: 12), 4), 0.9),
            Card(Npc("Momodi", Loc("Eastern La Noscea", 31.7f, 27.1f, mapId: 13), 5), 0.9),
            Card(Npc("Momodi", null, 6), 0.8),
            Card(Npc("Momodi", null, 7), 0.8),
        };

        var collapsed = EntityGrouper.Collapse(results);

        var card = Assert.IsType<EntityCardResult>(Assert.Single(collapsed));
        Assert.Equal(7, card.MergedCount);
        Assert.Equal(4, card.MergedLocations.Count);
        Assert.Equal(2, card.MergedHidden);
        Assert.Equal(1.0, card.Score);
    }

    [Fact]
    public void ScenePlacementsHideWhenAPrimaryCopyExists()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Momodi", Loc("Ul'dah - Steps of Nald", 11.7f, 9.7f), 1, handlers: 31), 1.0),
            Card(Npc("Momodi", Loc("New Gridania", 12.1f, 13.5f, mapId: 11), 2, handlers: 1), 0.9),
            Card(Npc("Momodi", Loc("Eastern La Noscea", 31.7f, 27.1f, mapId: 13), 3, handlers: 1), 0.9),
            Card(Npc("Momodi", null, 4), 0.8),
        };

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(results)));
        var location = Assert.Single(card.MergedLocations);
        Assert.Equal("Ul'dah - Steps of Nald", location.ZoneName);
        Assert.Equal(4, card.MergedCount);
        Assert.Equal(3, card.MergedHidden);
    }

    [Fact]
    public void AllModestCopiesKeepTheirLocations()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Reveler", Loc("Costa del Sol", 33.1f, 30.2f), 1, handlers: 1), 1.0),
            Card(Npc("Reveler", Loc("The Gold Court", 11.1f, 11.5f, mapId: 11), 2, handlers: 1), 0.9),
        };

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(results)));
        Assert.Equal(2, card.MergedLocations.Count);
        Assert.Equal(0, card.MergedHidden);
    }

    [Fact]
    public void UnplacedPrimaryStillHidesScenePlacements()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Momodi", null, 1, handlers: 31), 1.0),
            Card(Npc("Momodi", Loc("New Gridania", 12.1f, 13.5f), 2, handlers: 1), 0.9),
        };

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(results)));
        Assert.Empty(card.MergedLocations);
        Assert.Equal(2, card.MergedHidden);
    }

    [Fact]
    public void NearDuplicateCoordinatesCollapseToOneLine()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Momodi", Loc("Ul'dah", 11.68f, 9.71f), 1), 1.0),
            Card(Npc("Momodi", Loc("Ul'dah", 11.71f, 9.68f), 2), 0.9),
        };

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(results)));
        Assert.Single(card.MergedLocations);
        Assert.Equal(2, card.MergedCount);
    }

    [Fact]
    public void NameGroupingIgnoresCase()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Momodi", null, 1), 1.0),
            Card(Npc("momodi", null, 2), 0.9),
        };

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(results)));
        Assert.Equal(2, card.MergedCount);
    }

    [Fact]
    public void DistinctNamesStaySeparate()
    {
        var results = new SearchResult[] { Card(Npc("Momodi"), 1.0), Card(Npc("Nenekko"), 0.9) };

        Assert.Equal(2, EntityGrouper.Collapse(results).Count);
    }

    [Fact]
    public void SameNameDifferentEntityTypeStaysSeparate()
    {
        var results = new SearchResult[]
        {
            Card(Npc("Fenrir"), 1.0),
            Card(new MountEntity(5, "Fenrir"), 0.9),
        };

        Assert.Equal(2, EntityGrouper.Collapse(results).Count);
    }

    [Fact]
    public void SingletonsAndNonEntityResultsPassThroughInOrder()
    {
        var results = new SearchResult[]
        {
            WikiResult("First", 0.9),
            Card(Npc("Momodi"), 1.0),
            WikiResult("Second", 0.8),
        };

        var collapsed = EntityGrouper.Collapse(results);

        Assert.Equal(3, collapsed.Count);
        Assert.Equal("First", collapsed[0].Title);
        var card = Assert.IsType<EntityCardResult>(collapsed[1]);
        Assert.Equal(1, card.MergedCount);
        Assert.Equal("Second", collapsed[2].Title);
    }
}
