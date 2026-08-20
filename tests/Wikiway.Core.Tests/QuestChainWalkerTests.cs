using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;

namespace Wikiway.Core.Tests;

public class QuestChainWalkerTests
{
    [Fact]
    public void LinearChainWalksToTheRoot()
    {
        var quests = Chain(
            Quest(1, "Third", 30, Link(2, "Second")),
            Quest(2, "Second", 20, Link(3, "First")),
            Quest(3, "First", 10));

        var (steps, continues, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(["Second", "First"], steps.Select(s => s.Quest.Name));
        Assert.Equal([1, 2], steps.Select(s => s.Depth));
        Assert.Equal([(ushort)20, (ushort)10], steps.Select(s => s.Level));
        Assert.False(continues);
        Assert.Null(msq);
    }

    [Fact]
    public void AllJoinListsEverySiblingAtItsDepth()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Left"), Link(3, "Right")),
            Quest(2, "Left", 48),
            Quest(3, "Right", 49));

        var (steps, _, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(["Left", "Right"], steps.Select(s => s.Quest.Name));
        Assert.All(steps, s => Assert.Equal(1, s.Depth));
        Assert.All(steps, s => Assert.Equal(QuestJoin.All, s.Join));
    }

    [Fact]
    public void AnyJoinMarksStepsAsAny()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Left"), Link(3, "Right")) with { PrerequisiteJoin = QuestJoin.Any },
            Quest(2, "Left", 48),
            Quest(3, "Right", 49));

        var (steps, _, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.All(steps, s => Assert.Equal(QuestJoin.Any, s.Join));
    }

    [Fact]
    public void MsqPrerequisiteBecomesAMarkerStepAndIsNotDescended()
    {
        var quests = Chain(
            Quest(1, "Side Quest", 100, Link(2, "Finale")),
            Quest(2, "Finale", 100, Link(3, "Earlier Msq")) with { MainScenario = true, Expansion = "7.x" },
            Quest(3, "Earlier Msq", 99) with { MainScenario = true, Expansion = "7.x" });

        var (steps, continues, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var marker = Assert.Single(steps);
        Assert.Equal("7.x", marker.MsqVersion);
        Assert.Equal(1, marker.Depth);
        // Progress gating tests markers by row id, so the link must be real.
        Assert.Equal(2u, marker.Quest.RowId);
        Assert.False(continues);
        Assert.Equal("7.x", msq);
    }

    [Fact]
    public void MsqSiblingIsMarkedAtTheForkWhileTheQuestBranchIsWalked()
    {
        var quests = Chain(
            Quest(1, "Origin", 80, Link(2, "Finale"), Link(3, "Raid Finale")),
            Quest(2, "Finale", 80) with { MainScenario = true, Expansion = "5.x" },
            Quest(3, "Raid Finale", 70, Link(4, "Raid Start")),
            Quest(4, "Raid Start", 68));

        var (steps, _, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal("5.x", msq);
        Assert.Equal(3, steps.Count);
        Assert.Equal([1, 1, 2], steps.Select(s => s.Depth));
        Assert.Equal("5.x", steps[0].MsqVersion);
        Assert.Equal("Raid Finale", steps[1].Quest.Name);
        Assert.Equal("Raid Start", steps[2].Quest.Name);
    }

    [Fact]
    public void StepsCarryTheResolvedStartLocation()
    {
        var location = new MapLocation(1, 10, 12.5f, 8.2f, "Tuliyollal");
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Prereq")),
            Quest(2, "Prereq", 48) with { StartLocation = location });

        var (steps, _, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(location, Assert.Single(steps).StartLocation);
    }

    [Fact]
    public void CapsAtMaxSteps()
    {
        var quests = new Dictionary<uint, QuestEntity>();
        for (uint id = 1; id <= 30; id++)
            quests[id] = Quest(id, $"Quest {id}", 10, id < 30 ? [Link(id + 1)] : []);

        var (steps, continues, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(QuestChainWalker.MaxSteps, steps.Count);
        Assert.True(continues);
    }

    [Fact]
    public void UnresolvablePrerequisiteStopsCleanly()
    {
        var quests = Chain(Quest(1, "Origin", 50, new QuestLink(99, "Missing")));

        var (steps, continues, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var step = Assert.Single(steps);
        Assert.Equal("Missing", step.Quest.Name);
        Assert.Equal((ushort)0, step.Level);
        Assert.False(continues);
        Assert.Null(msq);
    }

    private static Dictionary<uint, QuestEntity> Chain(params QuestEntity[] quests) =>
        quests.ToDictionary(q => q.RowId);

    private static QuestEntity Quest(uint rowId, string name, ushort level, params QuestLink[] prerequisites) =>
        new(rowId, name, level, "Genre", prerequisites);

    private static QuestLink Link(uint rowId) => new(rowId, $"Quest {rowId}");

    private static QuestLink Link(uint rowId, string name) => new(rowId, name);
}
