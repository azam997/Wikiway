using Wikiway.Core.Models;

namespace Wikiway.Core.Pipeline;

public static class QuestChainWalker
{
    public const int MaxSteps = 20;

    // Walks the primary prerequisite path, listing every sibling at its depth.
    // Main-scenario quests become marker steps at the depth where the chain
    // forks into them but are never descended - only their expansion version
    // is shown (user decision: MSQ shows a patch, not a chain).
    public static (IReadOnlyList<QuestChainStep> Steps, bool Continues, string? MsqVersion) Walk(
        QuestEntity origin, Func<uint, QuestEntity?> resolve, int maxSteps = MaxSteps)
    {
        var steps = new List<QuestChainStep>();
        string? msqVersion = null;
        var current = origin;

        for (var depth = 1; current.Prerequisites.Count > 0; depth++)
        {
            QuestEntity? next = null;
            foreach (var link in current.Prerequisites)
            {
                if (steps.Count == maxSteps)
                    return (steps, true, msqVersion);

                var resolved = resolve(link.RowId);
                if (resolved is { MainScenario: true })
                {
                    msqVersion ??= resolved.Expansion;
                    steps.Add(new QuestChainStep(link, depth, current.PrerequisiteJoin, 0,
                        MsqVersion: resolved.Expansion));
                    continue;
                }

                steps.Add(new QuestChainStep(link, depth, current.PrerequisiteJoin, resolved?.ClassJobLevel ?? 0,
                    resolved?.StartLocation));
                next ??= resolved;
            }

            if (next == null)
                break;
            current = next;
        }

        return (steps, false, msqVersion);
    }
}
