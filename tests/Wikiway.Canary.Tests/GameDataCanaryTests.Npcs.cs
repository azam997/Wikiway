using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;

namespace Wikiway.Canary.Tests;

// NPC locations and same-name merging (store .Npcs.cs, Core EntityGrouper).
public partial class GameDataCanaryTests
{
    // The most breakage-prone path we have: ENpcResident -> Level -> Map ->
    // TerritoryType -> PlaceName, plus the coord math. Momodi has stood at the
    // Quicksand counter since 2013; if she moved, it's the schema that moved.
    [Fact]
    public void MomodiResolvesWithPlausibleMapCoords()
    {
        var store = fixture.Store();

        var momodi = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Npc && n.Name.StartsWith("momodi"));
        Assert.NotNull(momodi);

        var npc = store.GetNpc(momodi.RowId);
        Assert.NotNull(npc);
        Assert.NotNull(npc.Location);

        Assert.InRange(npc.Location.MapX, 1, 45);
        Assert.InRange(npc.Location.MapY, 1, 45);
        Assert.False(string.IsNullOrEmpty(npc.Location.ZoneName));
        Assert.Contains("ul'dah", npc.Location.ZoneName, StringComparison.OrdinalIgnoreCase);
    }

    // Checks the scene-spawn filter: Momodi has quest-scene copies in other
    // cities (single-quest event handlers) that must never surface as flaggable
    // locations - only the Quicksand post survives the merge.
    [Fact]
    public void MomodiCollapsesToHerQuicksandPostOnly()
    {
        var store = fixture.Store();

        var cards = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Npc && n.Name == "momodi")
            .Select(n => store.GetNpc(n.RowId))
            .Where(n => n != null)
            .Select(SearchResult (n) => new EntityCardResult
            {
                Title = n!.Name,
                Source = new Citation("Game data"),
                Entity = n,
                Score = 1.0,
            })
            .ToList();

        Assert.True(cards.Count > 1, "expected multiple momodi copies in ENpcResident");

        var card = Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(cards)));
        var location = Assert.Single(card.MergedLocations);
        Assert.Contains("ul'dah", location.ZoneName, StringComparison.OrdinalIgnoreCase);
        Assert.True(card.MergedHidden > 0, "expected scene copies to be hidden");

        // The hidden single-handler copies now name their gating quests.
        Assert.NotEmpty(card.CutsceneAppearances);
        Assert.All(card.CutsceneAppearances, a =>
        {
            Assert.False(string.IsNullOrEmpty(a.Quest.Name));
            Assert.False(string.IsNullOrEmpty(a.Expansion));
        });
        Assert.Contains(card.CutsceneAppearances, a => a.Location != null);
    }

    // Checks the story-NPC pin removal: Y'shtola leaves a placement per MSQ
    // stage (probed 2026-08-23: 415 rows, 136 with 2+ handlers across 56 zones)
    // and no handler pattern marks the current one, so the card must show no
    // pins at all - the wiki result answers "where is she", and her
    // single-quest copies still surface as gated cutscene appearances.
    [Fact]
    public void YshtolaCollapsesToASingleCardWithNoPins()
    {
        var card = CollapseNpc("y'shtola");

        Assert.True(card.MergedCount > 100, $"expected 100+ y'shtola copies, got {card.MergedCount}");
        Assert.Empty(card.MergedLocations);
        Assert.True(card.MergedHidden > 100, "expected the stage placements to be hidden");
        Assert.NotEmpty(card.CutsceneAppearances);
    }

    [Fact]
    public void RaubahnCollapsesToASingleCardWithNoPins()
    {
        var card = CollapseNpc("raubahn");

        Assert.InRange(card.MergedCount, 20, 300);
        Assert.Empty(card.MergedLocations);
        Assert.NotEmpty(card.CutsceneAppearances);
    }

    private EntityCardResult CollapseNpc(string name)
    {
        var store = fixture.Store();

        var cards = store.GetAllNames()
            .Where(n => n.Kind == EntityKind.Npc && n.Name == name)
            .Select(n => store.GetNpc(n.RowId))
            .Where(n => n != null)
            .Select(SearchResult (n) => new EntityCardResult
            {
                Title = n!.Name,
                Source = new Citation("Game data"),
                Entity = n,
                Score = 1.0,
            })
            .ToList();

        Assert.True(cards.Count > 1, $"expected multiple {name} copies in ENpcResident");
        return Assert.IsType<EntityCardResult>(Assert.Single(EntityGrouper.Collapse(cards)));
    }
}
