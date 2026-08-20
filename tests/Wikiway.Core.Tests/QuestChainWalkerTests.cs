using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Xunit;

namespace Wikiway.Core.Tests;

public class QuestChainWalkerTests
{
    [Fact]
    public void LinearChainWalksToTheRootInPlayOrder()
    {
        var quests = Chain(
            Quest(1, "Third", 30, Link(2, "Second")),
            Quest(2, "Second", 20, Link(3, "First")),
            Quest(3, "First", 10));

        var (chains, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var chain = Assert.Single(chains);
        Assert.Equal(["First", "Second"], chain.Steps.Select(s => s.Quest.Name));
        Assert.Equal([(ushort)10, (ushort)20], chain.Steps.Select(s => s.Level));
        Assert.Equal("Genre", chain.Genre);
        Assert.Null(chain.Gate);
        Assert.False(chain.Continues);
        Assert.Null(msq);
    }

    [Fact]
    public void OriginForkYieldsOneChainPerPrerequisite()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Left"), Link(3, "Right")),
            Quest(2, "Left", 48),
            Quest(3, "Right", 49, Link(4, "Right Root")),
            Quest(4, "Right Root", 47));

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(2, chains.Count);
        Assert.Equal(["Left"], chains[0].Steps.Select(s => s.Quest.Name));
        Assert.Equal(["Right Root", "Right"], chains[1].Steps.Select(s => s.Quest.Name));
        Assert.All(chains, c => Assert.Equal(QuestJoin.All, c.Join));
    }

    [Fact]
    public void AnyJoinMarksSiblingChainsAsAny()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Left"), Link(3, "Right")) with { PrerequisiteJoin = QuestJoin.Any },
            Quest(2, "Left", 48),
            Quest(3, "Right", 49));

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(2, chains.Count);
        Assert.All(chains, c => Assert.Equal(QuestJoin.Any, c.Join));
    }

    [Fact]
    public void MsqPrerequisiteBecomesAGateOnlyChainAndIsNotDescended()
    {
        var quests = Chain(
            Quest(1, "Side Quest", 100, Link(2, "Finale")),
            Quest(2, "Finale", 100, Link(3, "Earlier Msq")) with { MainScenario = true, Expansion = "7.x" },
            Quest(3, "Earlier Msq", 99) with { MainScenario = true, Expansion = "7.x" });

        var (chains, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var chain = Assert.Single(chains);
        Assert.Empty(chain.Steps);
        Assert.NotNull(chain.Gate);
        Assert.Equal("7.x", chain.Gate.Version);
        // Progress gating tests gates by row id, so the link must be real.
        Assert.Equal(2u, chain.Gate.Quest.RowId);
        Assert.Equal("7.x", msq);
    }

    [Fact]
    public void OriginForkWithMsqAppendsTheGateOnlyChainLast()
    {
        var quests = Chain(
            Quest(1, "Origin", 80, Link(2, "Finale"), Link(3, "Raid Finale")),
            Quest(2, "Finale", 80) with { MainScenario = true, Expansion = "5.x" },
            Quest(3, "Raid Finale", 70, Link(4, "Raid Start")),
            Quest(4, "Raid Start", 68));

        var (chains, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal("5.x", msq);
        Assert.Equal(2, chains.Count);
        Assert.Equal(["Raid Start", "Raid Finale"], chains[0].Steps.Select(s => s.Quest.Name));
        Assert.Null(chains[0].Gate);
        Assert.Empty(chains[1].Steps);
        Assert.Equal("5.x", chains[1].Gate?.Version);
    }

    [Fact]
    public void ForkAtDepthSpawnsTheBranchChainBeforeItsSpawner()
    {
        var quests = Chain(
            Quest(1, "Origin", 80, Link(2, "Trunk Late")),
            Quest(2, "Trunk Late", 80, Link(3, "Trunk Early")),
            Quest(3, "Trunk Early", 80, Link(4, "Msq Finale"), Link(5, "Branch Late")),
            Quest(4, "Msq Finale", 80) with { MainScenario = true, Expansion = "5.x" },
            Quest(5, "Branch Late", 70, Link(6, "Branch Early")),
            Quest(6, "Branch Early", 68, Link(7, "Older Msq")),
            Quest(7, "Older Msq", 67) with { MainScenario = true, Expansion = "4.x" });

        var (chains, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal("5.x", msq);
        Assert.Equal(2, chains.Count);
        Assert.Equal(["Branch Early", "Branch Late"], chains[0].Steps.Select(s => s.Quest.Name));
        Assert.Equal("4.x", chains[0].Gate?.Version);
        Assert.Equal(["Trunk Early", "Trunk Late"], chains[1].Steps.Select(s => s.Quest.Name));
        Assert.Equal("5.x", chains[1].Gate?.Version);
    }

    [Fact]
    public void StepsCarryTheResolvedStartLocation()
    {
        var location = new MapLocation(1, 10, 12.5f, 8.2f, "Tuliyollal");
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Prereq")),
            Quest(2, "Prereq", 48) with { StartLocation = location });

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(location, Assert.Single(Assert.Single(chains).Steps).StartLocation);
    }

    [Fact]
    public void CapsTotalStepsAtMaxSteps()
    {
        var quests = new Dictionary<uint, QuestEntity>();
        for (uint id = 1; id <= 30; id++)
            quests[id] = Quest(id, $"Quest {id}", 10, id < 30 ? [Link(id + 1)] : []);

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var chain = Assert.Single(chains);
        Assert.Equal(QuestChainWalker.MaxSteps, chain.Steps.Count);
        Assert.True(chain.Continues);
    }

    [Fact]
    public void UnresolvablePrerequisiteStopsCleanly()
    {
        var quests = Chain(Quest(1, "Origin", 50, new QuestLink(99, "Missing")));

        var (chains, msq) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        var chain = Assert.Single(chains);
        var step = Assert.Single(chain.Steps);
        Assert.Equal("Missing", step.Quest.Name);
        Assert.Equal((ushort)0, step.Level);
        Assert.False(chain.Continues);
        Assert.Null(msq);
    }

    [Fact]
    public void SharedAncestorAcrossBranchesEmitsOnce()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Left"), Link(3, "Right")),
            Quest(2, "Left", 48, Link(4, "Shared")),
            Quest(3, "Right", 49, Link(4, "Shared")),
            Quest(4, "Shared", 40));

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal(2, chains.Count);
        Assert.Equal(["Shared", "Left"], chains[0].Steps.Select(s => s.Quest.Name));
        Assert.Equal(["Right"], chains[1].Steps.Select(s => s.Quest.Name));
    }

    [Fact]
    public void MixedGenresLeaveTheChainLabelEmpty()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Prereq")),
            Quest(2, "Prereq", 48, Link(3, "Root")) with { Genre = "One Series" },
            Quest(3, "Root", 40) with { Genre = "Another Series" });

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id));

        Assert.Equal("", Assert.Single(chains).Genre);
    }

    [Fact]
    public void DroppedBranchMarksSpawnerAsContinuing()
    {
        var quests = Chain(
            Quest(1, "Origin", 50, Link(2, "Fork")),
            Quest(2, "Fork", 48, Link(3, "Left"), Link(4, "Right")),
            Quest(3, "Left", 40),
            Quest(4, "Right", 41));

        var (chains, _) = QuestChainWalker.Walk(quests[1u], id => quests.GetValueOrDefault(id), maxSteps: 2);

        Assert.Equal(2, chains.Count);
        Assert.Equal(["Left"], chains[0].Steps.Select(s => s.Quest.Name));
        Assert.Equal(["Fork"], chains[1].Steps.Select(s => s.Quest.Name));
        Assert.True(chains[1].Continues);
    }

    private static Dictionary<uint, QuestEntity> Chain(params QuestEntity[] quests) =>
        quests.ToDictionary(q => q.RowId);

    private static QuestEntity Quest(uint rowId, string name, ushort level, params QuestLink[] prerequisites) =>
        new(rowId, name, level, "Genre", prerequisites);

    private static QuestLink Link(uint rowId) => new(rowId, $"Quest {rowId}");

    private static QuestLink Link(uint rowId, string name) => new(rowId, name);
}
