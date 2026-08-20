using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class QuestChainWalker
{
    public const int MaxSteps = 20;

    // Walks every prerequisite branch into its own chain, steps in play order
    // (first pick-up-able quest first). A fork ends the current chain and
    // spawns one chain per branch, appended before their spawner so the list
    // reads in play order. Main-scenario quests become chain-level gates and
    // are never descended (user decision: MSQ shows a patch, not a chain).
    public static (IReadOnlyList<QuestChain> Chains, string? MsqVersion) Walk(
        QuestEntity origin, Func<uint, QuestEntity?> resolve, int maxSteps = MaxSteps)
    {
        var chains = new List<QuestChain>();
        var gateOnly = new List<QuestChain>();
        var visited = new HashSet<uint> { origin.RowId };
        var budget = maxSteps;
        string? msqVersion = null;

        var roots = new List<(QuestLink Link, QuestEntity? Resolved)>();
        foreach (var link in origin.Prerequisites)
        {
            var resolved = resolve(link.RowId);
            if (resolved is { MainScenario: true })
            {
                msqVersion ??= resolved.Expansion;
                // MSQ is linear, so two prerequisites gated on the same patch
                // would render as duplicate Main Scenario lines.
                if (!gateOnly.Exists(c => c.Gate!.Version == resolved.Expansion))
                    gateOnly.Add(new QuestChain([], new MsqGate(link, resolved.Expansion), Join: origin.PrerequisiteJoin));
                continue;
            }

            roots.Add((link, resolved));
        }

        foreach (var root in roots)
            WalkBranch(root.Link, root.Resolved, origin.PrerequisiteJoin);

        chains.AddRange(gateOnly);
        return (chains, msqVersion);

        void WalkBranch(QuestLink link, QuestEntity? resolved, QuestJoin join)
        {
            var steps = new List<QuestChainStep>();
            var genres = new HashSet<string>();
            var spawned = new List<(QuestLink Link, QuestEntity? Resolved, QuestJoin Join)>();
            MsqGate? gate = null;
            var continues = false;

            while (true)
            {
                if (budget == 0)
                {
                    continues = true;
                    break;
                }

                if (!visited.Add(link.RowId))
                    break;

                steps.Add(new QuestChainStep(link, resolved?.ClassJobLevel ?? 0, resolved?.StartLocation));
                budget--;
                if (resolved == null)
                    break;
                genres.Add(resolved.Genre);
                if (resolved.Prerequisites.Count == 0)
                    break;

                var branches = new List<(QuestLink Link, QuestEntity? Resolved)>();
                foreach (var prereq in resolved.Prerequisites)
                {
                    var prereqResolved = resolve(prereq.RowId);
                    if (prereqResolved is { MainScenario: true })
                    {
                        msqVersion ??= prereqResolved.Expansion;
                        gate ??= new MsqGate(prereq, prereqResolved.Expansion);
                        continue;
                    }

                    branches.Add((prereq, prereqResolved));
                }

                if (resolved.Prerequisites.Count == 1 && branches.Count == 1)
                {
                    (link, resolved) = branches[0];
                    continue;
                }

                foreach (var branch in branches)
                    spawned.Add((branch.Link, branch.Resolved, resolved.PrerequisiteJoin));
                break;
            }

            foreach (var branch in spawned)
            {
                if (visited.Contains(branch.Link.RowId))
                    continue;

                if (budget == 0)
                {
                    continues = true;
                    continue;
                }

                WalkBranch(branch.Link, branch.Resolved, branch.Join);
            }

            if (steps.Count == 0)
                return;

            steps.Reverse();
            var genre = genres.Count == 1 ? genres.Single() : "";
            chains.Add(new QuestChain(steps, gate, genre, join, continues));
        }
    }
}
