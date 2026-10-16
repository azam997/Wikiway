using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// UNLOCK REQUIREMENTS blocks: a quest's prerequisite chains (built by
// QuestChainWalker) and a duty's unlock quest. Also the quest-step line and
// its [Flag][View] buttons, shared with aether currents and cutscene lists.
public partial class MainWindow
{
    private void DrawUnlockChain(QuestEntity quest)
    {
        ImGui.Spacing();
        DetailLabel("UNLOCK REQUIREMENTS");

        for (var i = 0; i < quest.UnlockChains.Count; i++)
        {
            var chain = quest.UnlockChains[i];
            // A gate-only chain is a bare main-scenario prerequisite: one line, no block.
            if (chain.Steps.Count == 0)
            {
                if (chain.Gate is { } gate)
                    MsqLine(gate);
                continue;
            }

            if (i > 0)
                ImGui.Spacing();
            DrawChainBlock(quest, chain, i);
        }
    }

    // One prerequisite branch, folded behind a summary button such as
    // "Resistance Weapons: 8 quests (3 done) + Main Scenario (5.x)". Opened,
    // it lists the steps in play order with a flag on the next one to do.
    private void DrawChainBlock(QuestEntity quest, QuestChain chain, int index)
    {
        var fonts = plugin.Fonts;
        var progress = plugin.QuestProgress;

        var key = (quest.RowId, index);
        var open = session.ExpandedChains.Contains(key);
        var label = chain.Genre.Length > 0 ? chain.Genre : "Quest chain";
        var count = chain.Steps.Count == 1 ? "1 quest" : $"{chain.Steps.Count} quests";
        var summary = $"{CaretGlyph(open)} {label}: {count}";
        if (progress.IsAvailable)
            summary += $" ({chain.Steps.Count(s => progress.IsComplete(s.Quest.RowId))} done)";
        if (chain.Gate is { } gateSummary)
            summary += $" + Main Scenario ({gateSummary.Version})";
        if (chain.Join == QuestJoin.Any)
            summary += " (either)";

        using (fonts.Body13.Push())
        {
            if (Widgets.GhostButton($"{summary}##chaintoggle{quest.RowId}-{index}") && !session.ExpandedChains.Remove(key))
                session.ExpandedChains.Add(key);
        }

        if (!open)
            return;

        // Truncation drops the earliest quests, so the pointer to the rest
        // sits above the first listed step.
        if (chain.Continues)
            TextLine("chain continues - open the first quest to follow it further", fonts.Citation12, Theme.Neutral600);

        if (chain.Gate is { } gate)
            MsqLine(gate);

        // Only the next incomplete step gets a flag; logged out, that's step 1.
        var nextIdx = 0;
        if (progress.IsAvailable)
        {
            nextIdx = -1;
            for (var j = 0; j < chain.Steps.Count; j++)
            {
                if (!progress.IsComplete(chain.Steps[j].Quest.RowId))
                {
                    nextIdx = j;
                    break;
                }
            }
        }

        var viewWidth = FlagButtonWidth("View");
        for (var j = 0; j < chain.Steps.Count; j++)
        {
            var step = chain.Steps[j];
            DrawQuestStep(step, $"{j + 1}.");
            QuestStepButtons(step.Quest.Name, j == nextIdx ? step.StartLocation : null,
                "chain", $"{quest.RowId}-{index}-{j}", viewWidth);
        }
    }

    // Quest name, level and completion checkmark; `number` prefixes chain steps.
    private void DrawQuestStep(QuestChainStep step, string number = "")
    {
        using var font = plugin.Fonts.Body13.Push();
        if (number.Length > 0)
        {
            ColorText(number, Theme.Neutral600);
            InlineText(step.Quest.Name, Theme.Neutral300);
        }
        else
        {
            ColorText(step.Quest.Name, Theme.Neutral300);
        }

        if (step.Level > 0)
            InlineText($"Lv {step.Level}", Theme.Neutral600);

        StepCheckmark(step.Quest.RowId);
    }

    // Compact [Flag] (when flagAt is set) and [View] at the end of a quest
    // line. Ids are "{idPrefix}flag{idSuffix}" and "{idPrefix}{idSuffix}";
    // View searches the quest.
    private void QuestStepButtons(string questName, MapLocation? flagAt, string idPrefix, string idSuffix, float viewWidth)
    {
        using var font = plugin.Fonts.Citation12.Push();
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ButtonPadding);
        if (flagAt is { } start)
            DrawLocationActions(start, $"{idPrefix}flag{idSuffix}", compact: true,
                reserved: viewWidth + (Theme.Space2 * ImGuiHelpers.GlobalScale));

        if (RightViewButton($"{idPrefix}{idSuffix}", viewWidth))
            queuedNavigation = (questName, SearchCategory.Unlocks);
    }

    private void MsqLine(MsqGate gate)
    {
        using var font = plugin.Fonts.Body13.Push();
        IconLine(FontAwesomeIcon.Lock, $"Main Scenario ({gate.Version})");
        StepCheckmark(gate.Quest.RowId);
    }

    // Checkmarks follow quest progress only; spoiler settings don't hide them.
    private void StepCheckmark(uint questRowId)
    {
        if (plugin.QuestProgress.IsComplete(questRowId))
            InlineText(FontAwesomeIcon.Check.ToIconString(), Theme.Highlight, Theme.Space2);
    }

    // "Unlocked by quest: X" with [First quest] (start of X's chain) and
    // [View quest] buttons.
    private void DrawDutyUnlock(DutyEntity duty)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("UNLOCK REQUIREMENTS");

        // A main-scenario unlock quest's name stays withheld even after the
        // card itself is revealed: revealing the duty doesn't reveal story
        // quest names.
        var msqUnlock = duty.MsqGate is { } gate
            && gate.Quest.RowId == duty.UnlockQuest!.RowId
            && IsMsqTitleHidden(gate.Quest.RowId);

        using (fonts.Body13.Push())
        {
            GlyphPrefix(FontAwesomeIcon.Lock);
            if (msqUnlock)
            {
                ColorText("Unlocked by quest:", Theme.Neutral300);
                ImGui.SameLine(0, Theme.Space2 * scale);
                Widgets.BlurredText(duty.UnlockQuest!.Name);
                InlineText($"Main Scenario ({duty.MsqGate!.Version})", Theme.Neutral600);
            }
            else
            {
                ColorText($"Unlocked by quest: {duty.UnlockQuest!.Name}", Theme.Neutral300);
            }
        }

        if (msqUnlock)
            return;

        using var font = fonts.Citation12.Push();
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ButtonPadding);
        var viewWidth = FlagButtonWidth("View quest");
        if (duty.ChainStart is { } chainStart)
        {
            var firstWidth = FlagButtonWidth("First quest");
            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - viewWidth - (Theme.Space2 * scale) - firstWidth);
            if (Widgets.OutlinedButton($"First quest##dutychainstart{duty.RowId}"))
                queuedNavigation = (chainStart.Name, SearchCategory.Unlocks);
        }

        ImGui.SameLine();
        ImGui.SetCursorPosX(RowRightEdge() - viewWidth);
        if (Widgets.OutlinedButton($"View quest##dutyunlock{duty.RowId}"))
            queuedNavigation = (duty.UnlockQuest.Name, SearchCategory.Unlocks);
    }
}
