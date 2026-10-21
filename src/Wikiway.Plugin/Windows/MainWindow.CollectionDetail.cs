using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// Expanded detail for the collection and world kinds: mounts, minions,
// orchestrion rolls, Triple Triad cards, emotes, sightseeing vistas, aether
// currents, FATEs and levequests.
public partial class MainWindow
{
    // Mount/minion/orchestrion/card/emote cards answer "how do I get this
    // non-item thing" by borrowing the teaching item's acquisition sources.
    private void DrawCollectionDetail(EntityCardResult card)
    {
        var (teaching, flavor) = card.Entity switch
        {
            MountEntity mount => (mount.TeachingItem, mount.Description),
            MinionEntity minion => (minion.TeachingItem, minion.Description),
            OrchestrionEntity orchestrion => (orchestrion.TeachingItem, orchestrion.Description),
            TripleTriadCardEntity tripleTriad => (tripleTriad.TeachingItem, tripleTriad.Description),
            EmoteEntity emote => (emote.TeachingItem, (string?)null),
            _ => (null, null),
        };
        if (flavor == null && teaching == null && card.Entity is not EmoteEntity)
            return;

        var fonts = plugin.Fonts;

        if (card.Entity is MinionEntity { BattleStats: { } stats })
        {
            ImGui.Spacing();
            var special = stats.SpecialAction.Length > 0 ? $" · Special: {stats.SpecialAction}" : "";
            TextLine($"Lord of Verminion — HP {stats.Hp} · Attack {stats.Attack} · " +
                $"Defense {stats.Defense} · Speed {stats.Speed} · Cost {stats.Cost}{special}",
                fonts.Body13, Theme.Neutral300, wrap: true);
        }

        var cardSources = card.Entity is TripleTriadCardEntity
            { NpcName.Length: > 0 } or TripleTriadCardEntity { DutyName.Length: > 0 }
            or TripleTriadCardEntity { ObtainText.Length: > 0 };
        var emoteQuest = card.Entity is EmoteEntity { UnlockQuest: not null };
        if (teaching != null || cardSources || emoteQuest)
        {
            ImGui.Spacing();
            DetailLabel("ACQUISITION");

            if (card.Entity is TripleTriadCardEntity tt)
                DrawCardObtain(tt);

            if (card.Entity is EmoteEntity { UnlockQuest: { } unlockQuest })
                LinkLine(FontAwesomeIcon.Scroll, "Unlocked by quest", unlockQuest.Name, SearchCategory.Unlocks);

            if (teaching != null)
            {
                LinkLine(FontAwesomeIcon.Book, card.Entity is EmoteEntity ? "Unlocked by item" : "Taught by item",
                    teaching.Name, SearchCategory.Items);

                if (LoadedItemDetails(teaching.RowId) is not { } details)
                    LoadingLine();
                else if (details.Acquisition is { } sources)
                    DrawAcquisitionSources(card.Entity.RowId, sources);
            }
        }

        if (flavor is { Length: > 0 })
        {
            ImGui.Spacing();
            TextLine(flavor, fonts.Citation12, Theme.Neutral500, wrap: true);
        }
    }

    // Where a Triple Triad card comes from: an NPC to beat, a duty drop,
    // and/or the game's own obtain text.
    private void DrawCardObtain(TripleTriadCardEntity tripleTriad)
    {
        if (tripleTriad.NpcName.Length > 0)
        {
            using (plugin.Fonts.Body13.Push())
            {
                GlyphPrefix(FontAwesomeIcon.MapPin);
                ImGui.TextUnformatted($"Won from {tripleTriad.NpcName}");
                if (tripleTriad.NpcLocation is { } loc)
                    LocationTail(loc, LocationActionsWidth(loc));
            }

            if (tripleTriad.NpcLocation is { } flagLoc)
                LocationButtons(flagLoc, $"ttnpc{tripleTriad.RowId}");
        }

        if (tripleTriad.DutyName.Length > 0)
            DetailLine(FontAwesomeIcon.Dungeon, $"Dropped in {tripleTriad.DutyName}");

        if (tripleTriad.ObtainText.Length > 0)
            DetailLine(FontAwesomeIcon.InfoCircle, tripleTriad.ObtainText);
    }

    private void DrawVistaDetail(VistaEntity vista)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("SIGHTSEEING LOG");

        using (fonts.Body13.Push())
        {
            if (vista.Hint.Length > 0)
                ColorText(vista.Hint, Theme.Neutral300, wrap: true);
            if (vista.Emote.Length > 0)
                IconLine(FontAwesomeIcon.Eye, $"Emote: {vista.Emote}");
            if (vista.TimeWindow.Length > 0)
                IconLine(FontAwesomeIcon.Clock, vista.TimeWindow);
        }

        if (vista.Lore.Length > 0)
        {
            ImGui.Spacing();
            TextLine(vista.Lore, fonts.Citation12, Theme.Neutral500, wrap: true);
        }
    }

    // The zone's quest-gated currents as quest steps. Field currents are left
    // to the wiki: many aren't tied to any zone set in the sheets.
    private void DrawAetherCurrentsDetail(AetherCurrentZoneEntity currents)
    {
        ImGui.Spacing();
        DetailLabel("UNLOCK QUESTS");

        var viewWidth = FlagButtonWidth("View");
        for (var i = 0; i < currents.QuestCurrents.Count; i++)
        {
            var step = currents.QuestCurrents[i];
            DrawQuestStep(step);
            QuestStepButtons(step.Quest.Name, step.StartLocation, "current", $"{currents.RowId}-{i}", viewWidth);
        }

        TextLine("Field currents are scattered in the open world - the wiki page lists them.",
            plugin.Fonts.Citation12, Theme.Neutral600);
    }

    private void DrawFateRequirement(QuestLink quest)
    {
        ImGui.Spacing();
        DetailLabel("REQUIREMENTS");
        LinkLine(FontAwesomeIcon.Scroll, "Requires quest", quest.Name, SearchCategory.Unlocks);
    }

    private void DrawLeveDetail(LeveEntity leve)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("LEVEQUEST");

        using var font = fonts.Body13.Push();
        if (leve.Description.Length > 0)
            ColorText(leve.Description, Theme.Neutral300, wrap: true);

        var reward = leve.AllowanceCost == 1 ? "1 leve allowance" : $"{leve.AllowanceCost} leve allowances";
        if (leve.ExpReward > 0)
            reward += $" · {leve.ExpReward} exp";
        if (leve.GilReward > 0)
            reward += $" · {leve.GilReward} gil";
        IconLine(FontAwesomeIcon.Scroll, reward);
    }
}
