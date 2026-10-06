using Wikiway.Core.Abstractions;
using Xunit;

namespace Wikiway.Canary.Tests;

// Quest prerequisites and unlock chains (store .Quests.cs, Core
// QuestChainWalker).
public partial class GameDataCanaryTests
{
    [Fact]
    public void UltimateWeaponQuestHasResolvablePrerequisites()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "the ultimate weapon");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.NotEmpty(quest.Prerequisites);

        // Walk the chain far enough to prove the refs actually resolve.
        var current = quest;
        for (var depth = 0; depth < 10 && current.Prerequisites.Count > 0; depth++)
        {
            var previous = store.GetQuest(current.Prerequisites[0].RowId);
            Assert.NotNull(previous);
            Assert.False(string.IsNullOrEmpty(previous.Name));
            current = previous;
        }
    }

    // Checks the unlock-chain path: CFC UnlockCriteria -> Quest ->
    // PreviousQuest walk with the MSQ boundary. South Horn is a field area
    // whose chain is a few side quests ending in a Dawntrail MSQ (7.x) marker
    // as of patch 7.3.
    [Fact]
    public void OccultCrescentUnlockChainResolvesAndStopsAtMsq()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Unlockable && n.Name == "the occult crescent: south horn");
        Assert.NotNull(entry);

        var duty = store.GetDuty(entry.RowId);
        Assert.NotNull(duty);
        Assert.True(duty.FieldArea, "field-area detection broke");
        Assert.NotNull(duty.UnlockQuest);
        Assert.Equal("Unfamiliar Territory", duty.UnlockQuest.Name);

        var quest = store.GetQuest(duty.UnlockQuest.RowId);
        Assert.NotNull(quest);
        var chain = Assert.Single(quest.UnlockChains);
        Assert.InRange(chain.Steps.Count, 2, 10);
        Assert.All(chain.Steps, s => Assert.False(string.IsNullOrEmpty(s.Quest.Name)));
        Assert.Equal("7.x", quest.MsqRequirement);
        Assert.Equal("7.x", chain.Gate?.Version);
        Assert.False(chain.Continues);

        // Quest.IssuerLocation -> Level -> map coords, and the duty's shortcut
        // to the chain's first pick-up-able quest.
        Assert.Equal(chain.Steps[0].Quest.Name, duty.ChainStart?.Name);
        Assert.All(chain.Steps, s => Assert.NotNull(s.StartLocation));
        Assert.NotNull(quest.StartLocation);
        Assert.InRange(quest.StartLocation.MapX, 1, 45);
        Assert.InRange(quest.StartLocation.MapY, 1, 45);
        Assert.False(string.IsNullOrEmpty(quest.StartLocation.ZoneName));
    }

    // "The Bozja Incident" forks at "Hail to the Queen": Shadowbringers MSQ
    // (5.x) AND the Return to Ivalice finale. Each branch must be its own
    // chain in play order, dependency chain listed before its spawner.
    [Fact]
    public void BozjaIncidentForksIntoMsqAndIvalice()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "the bozja incident");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.Equal("5.x", quest.MsqRequirement);
        Assert.Equal(2, quest.UnlockChains.Count);

        var ivalice = quest.UnlockChains[0];
        Assert.Equal("Return to Ivalice", ivalice.Genre);
        Assert.InRange(ivalice.Steps.Count, 6, 10);
        Assert.Equal("The City of Lost Angels", ivalice.Steps[^1].Quest.Name);
        Assert.Equal("4.x", ivalice.Gate?.Version);

        var trunk = quest.UnlockChains[1];
        Assert.Equal("Resistance Weapons", trunk.Genre);
        Assert.Equal(["Hail to the Queen", "Path to the Past"], trunk.Steps.Select(s => s.Quest.Name));
        Assert.Equal("5.x", trunk.Gate?.Version);
    }

    // "Dawntrail" (the 7.0 finale) is itself MSQ, so the boundary rule
    // collapses its whole ancestry into MSQ marker steps and nothing else.
    // "The Ultimate Weapon" is unusable as an anchor - an event quest shares
    // the exact name and wins the index lookup.
    [Fact]
    public void MsqQuestChainCollapsesToItsPatchRequirement()
    {
        var store = fixture.Store();

        var entry = store.GetAllNames()
            .FirstOrDefault(n => n.Kind == EntityKind.Quest && n.Name == "dawntrail");
        Assert.NotNull(entry);

        var quest = store.GetQuest(entry.RowId);
        Assert.NotNull(quest);
        Assert.True(quest.MainScenario, "MSQ category detection broke");
        Assert.NotEmpty(quest.UnlockChains);
        Assert.All(quest.UnlockChains, c =>
        {
            Assert.Empty(c.Steps);
            Assert.Equal("7.x", c.Gate?.Version);
        });
        Assert.Equal("7.x", quest.MsqRequirement);
    }
}
