using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Wikiway.Core.Models;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// Expand/collapse and spoiler rules for game-data cards, and the order in
// which an expanded card stacks its detail blocks.
public partial class MainWindow
{
    // Whether a card has anything to show when expanded. Drives the caret,
    // the click-to-expand row, and auto-expanding the top result; a
    // spoiler-gated card never expands.
    private bool HasDetail(EntityCardResult card) =>
        !IsSpoilerGated(card)
        && (card.WikiSections.Count > 0
            || card.Entity is ItemEntity { Acquisition: not null }
            || (plugin.Configuration.ShowEquipmentStats
                && card.Entity is ItemEntity { Equipment: not null } or ItemEntity { Food: not null })
            || (plugin.Configuration.ShowItemUsage && card.Entity is ItemEntity { Usage: not null })
            || ShowNpcTabs(card)
            || card.Entity is MountEntity { TeachingItem: not null } or MountEntity { Description.Length: > 0 }
                or MinionEntity { TeachingItem: not null } or MinionEntity { BattleStats: not null }
                or MinionEntity { Description.Length: > 0 }
                or OrchestrionEntity { TeachingItem: not null } or OrchestrionEntity { Description.Length: > 0 }
                or TripleTriadCardEntity
                or EmoteEntity { TeachingItem: not null } or EmoteEntity { UnlockQuest: not null }
                or VistaEntity
                or AetherCurrentZoneEntity { QuestCurrents.Count: > 0 }
                or FateEntity { RequiredQuest: not null }
                or LeveEntity { Description.Length: > 0 }
            || (plugin.Configuration.ShowUnlockRequirements
                && card.Entity is QuestEntity { UnlockChains.Count: > 0 }
                    or QuestEntity { MsqRequirement: not null }
                    or DutyEntity { UnlockQuest: not null }));

    // Spoiler protection only acts when it's switched on and a quest-progress
    // snapshot exists; logged out, everything shows (fails open).
    private bool GatingActive =>
        plugin.Configuration.SpoilerProtectionEnabled && plugin.QuestProgress.IsAvailable;

    // A duty or area whose main-scenario gate the player hasn't finished,
    // and that they haven't revealed this session.
    private bool IsSpoilerGated(EntityCardResult card) =>
        GatingActive
        && card.Entity is DutyEntity { MsqGate: { } gate }
        && !plugin.QuestProgress.IsComplete(gate.Quest.RowId)
        && !revealedSpoilers.Contains(RowKey(card));

    // An accepted quest's name is already in the player's journal, so only
    // unreached main-scenario titles are withheld.
    private bool IsMsqTitleHidden(uint questRowId) =>
        GatingActive
        && !plugin.QuestProgress.IsComplete(questRowId)
        && !plugin.QuestProgress.IsAccepted(questRowId);

    private bool ShowNpcTabs(EntityCardResult card) =>
        plugin.Configuration.ShowCutsceneAppearances
        && card is { Entity: NpcEntity, CutsceneAppearances.Count: > 0 };

    // Identifies a card across frames for expand state and spoiler reveals.
    private static string RowKey(EntityCardResult card) => $"{card.Entity.GetType().Name}:{card.Title}";

    // The expand caret at the right of the header line.
    private void RowDetailControls(EntityCardResult card)
    {
        if (!HasDetail(card))
            return;

        var key = RowKey(card);
        var open = session.ExpandedRows.Contains(key);
        if (HeaderAction(CaretGlyph(open), $"expand{key}") && !session.ExpandedRows.Remove(key))
            session.ExpandedRows.Add(key);
    }

    // Each block checks its own data and settings, so a card draws only the
    // blocks that apply to it.
    private void DrawCardDetail(EntityCardResult card)
    {
        if (card.Entity is NpcEntity npcEntity && ShowNpcTabs(card))
            DrawNpcTabs(card, npcEntity);

        if (plugin.Configuration.ShowEquipmentStats && card.Entity is ItemEntity { Equipment: { } equipment })
            DrawEquipment(equipment);

        if (plugin.Configuration.ShowEquipmentStats && card.Entity is ItemEntity { Food: { } food })
            DrawConsumable(food);

        if (card.Entity is ItemEntity { Acquisition: { } itemAcquisition })
        {
            ImGui.Spacing();
            DetailLabel("ACQUISITION");
            DrawAcquisitionSources(card.Entity.RowId, itemAcquisition);
        }

        if (plugin.Configuration.ShowItemUsage && card.Entity is ItemEntity { Usage: { } usage })
            DrawUsage(usage);

        DrawCollectionDetail(card);

        if (card.Entity is VistaEntity vista)
            DrawVistaDetail(vista);

        if (card.Entity is AetherCurrentZoneEntity { QuestCurrents.Count: > 0 } currents)
            DrawAetherCurrentsDetail(currents);

        if (card.Entity is FateEntity { RequiredQuest: { } fateQuest })
            DrawFateRequirement(fateQuest);

        if (card.Entity is LeveEntity leve)
            DrawLeveDetail(leve);

        DrawTrailingDetail(card);
    }

    // Unlock requirements, then any wiki sections ResultJoiner attached to
    // this card.
    private void DrawTrailingDetail(EntityCardResult card)
    {
        if (plugin.Configuration.ShowUnlockRequirements)
        {
            if (card.Entity is QuestEntity quest && (quest.UnlockChains.Count > 0 || quest.MsqRequirement != null))
                DrawUnlockChain(quest);
            if (card.Entity is DutyEntity { UnlockQuest: not null } duty)
                DrawDutyUnlock(duty);
        }

        if (card.WikiSections.Count > 0)
        {
            ImGui.Spacing();
            DetailLabel("FROM THE WIKI");
            if (card.WikiUrl is { } url)
            {
                using var font = plugin.Fonts.Citation12.Push();
                using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ButtonPadding);
                var width = FlagButtonWidth("Open");
                ImGui.SameLine();
                ImGui.SetCursorPosX(RowRightEdge() - width);
                if (Widgets.OutlinedButton($"Open##wikidetail{card.Title}"))
                    BrowserOpener.Open(url);
            }

            DrawSectionList(card.Title, card.WikiSections);
        }
    }
}
