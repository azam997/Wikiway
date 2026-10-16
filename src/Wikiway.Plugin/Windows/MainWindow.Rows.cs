using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Wikiway.Core.Models;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// The collapsed face of each result row, one method per kind of result:
// header line (title, tags, source, buttons) plus a line or two of summary.
// Most rows build their header with RowHeader (MainWindow.RowParts.cs); quest
// and duty rows spell theirs out because spoiler gating changes the title and
// tags. What appears below once a row is expanded lives in the *Detail files.
public partial class MainWindow
{
    private void DrawNpcRow(EntityCardResult card, NpcEntity npc)
    {
        RowHeader(card, npc.Name, "NPC");

        var locations = NpcLocations(card, npc);
        if (card.MergedCount > 1)
        {
            using var font = plugin.Fonts.Small11.Push();
            var note = $"{card.MergedCount} ROWS MERGED · {locations.Count} WITH A LOCATION";
            ImGui.SameLine();
            ImGui.SetCursorPosX(headerRight - ImGui.CalcTextSize(note).X);
            ColorText(note, Theme.Neutral600);
        }

        // Expanded cards show the same list inside the Locations tab instead,
        // so the row draws it only while collapsed.
        if (ShowNpcTabs(card) && session.ExpandedRows.Contains(RowKey(card)))
            return;

        DrawNpcLocations(npc.RowId, locations, card.MergedHidden);
    }

    private void DrawItemRow(EntityCardResult card, ItemEntity item)
    {
        var gearTag = plugin.Configuration.ShowEquipmentStats && item.Equipment is { } gear
            ? $"ilvl {gear.ItemLevel} · Lv. {gear.EquipLevel}"
            : "";
        var materiaTag = plugin.Configuration.ShowItemUsage && item.Usage is { } usage ? usage.MateriaTag : "";

        using var row = IconRow(item.Icon);
        RowHeader(card, item.Name, Label("Item", item.Category), gearTag, materiaTag);

        if (item.Description.Length > 0)
            EllipsizedLine(item.Description);

        TextLine(item.Marketable ? "Marketboard: yes" : "Marketboard: no", plugin.Fonts.Citation12, Theme.Neutral600);
    }

    private void DrawQuestRow(EntityCardResult card, QuestEntity quest)
    {
        var msqHidden = quest.MainScenario && IsMsqTitleHidden(quest.RowId);
        if (msqHidden)
        {
            // The genre name alone dates the quest, so the tag collapses to
            // the patch while the title is withheld.
            BlurredRowTitle(quest.Name);
            RowTag($"Main Scenario ({quest.Expansion})", TagStyle.Accent);
        }
        else
        {
            RowTitle(quest.Name);
            RowTag(Label("Quest", quest.Genre), TagStyle.Accent);
        }

        if (quest.ClassJobLevel > 0)
            RowTag($"Level {quest.ClassJobLevel}", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (quest.StartLocation is { } start && !msqHidden)
            PinnedLocationLine($"Starts: {start.ZoneName}", start, $"queststart{quest.RowId}");

        // The expanded card shows the full chain, so the one-line summary
        // only appears while collapsed.
        var chainShown = plugin.Configuration.ShowUnlockRequirements && session.ExpandedRows.Contains(RowKey(card));
        if (quest.Prerequisites.Count > 0 && !chainShown && !msqHidden)
        {
            var joiner = quest.PrerequisiteJoin == QuestJoin.Any ? " or " : ", ";
            TextLine("Requires: " + string.Join(joiner, quest.Prerequisites.Select(p => PrerequisiteLabel(quest, p))),
                plugin.Fonts.Body13, Theme.Neutral400, wrap: true);
        }
    }

    // Unreached main-scenario prerequisites show as their patch instead of
    // their name; the chain gates identify which links those are.
    private string PrerequisiteLabel(QuestEntity quest, QuestLink prerequisite)
    {
        var gate = quest.UnlockChains
            .Select(c => c.Gate)
            .FirstOrDefault(g => g?.Quest.RowId == prerequisite.RowId);
        return gate != null && IsMsqTitleHidden(prerequisite.RowId)
            ? $"Main Scenario ({gate.Version})"
            : prerequisite.Name;
    }

    // Duties and unlockable areas. Past the player's MSQ progress the title
    // blurs until they click the reveal button (see IsSpoilerGated).
    private void DrawDutyRow(EntityCardResult card, DutyEntity duty)
    {
        var gated = IsSpoilerGated(card);
        if (gated)
            BlurredRowTitle(duty.Name);
        else
            RowTitle(duty.Name);

        RowTag(Label(duty.FieldArea ? "Area" : "Duty", duty.ContentType), TagStyle.Accent);
        if (duty.ClassJobLevel > 0)
        {
            RowTag(duty.ItemLevel > 0
                ? $"Level {duty.ClassJobLevel} · ilvl {duty.ItemLevel}"
                : $"Level {duty.ClassJobLevel}", TagStyle.Outline);
        }

        if (duty.HighEnd)
            RowTag("High-end", TagStyle.Accent);
        if (duty.Solo)
            RowTag("Solo", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (gated)
        {
            using var font = plugin.Fonts.Citation12.Push();
            if (Widgets.GhostButton($"Show content from beyond MSQ progress - may contain spoilers##reveal{duty.RowId}"))
                revealedSpoilers.Add(RowKey(card));
        }
    }

    private void DrawAchievementRow(EntityCardResult card, AchievementEntity achievement)
    {
        RowHeader(card, achievement.Name, Label("Achievement", achievement.Category));

        if (achievement.Description.Length > 0)
            TextLine(achievement.Description, plugin.Fonts.Body13, Theme.Neutral400, wrap: true);
    }

    // Shared by mounts, minions and orchestrion rolls: icon tile, title, tag,
    // and the flavor text ellipsized while collapsed.
    private void DrawIconTitleRow(EntityCardResult card, string tag, ushort icon, string description = "")
    {
        using var row = IconRow(icon);
        RowHeader(card, card.Entity.Name, tag);
        CollapsedSummary(card, description);
    }

    private void DrawTripleTriadRow(EntityCardResult card, TripleTriadCardEntity tripleTriad)
    {
        var typeTag = tripleTriad.CardType.Length > 0 ? $"Triple Triad · {tripleTriad.CardType}" : "Triple Triad card";

        using var row = IconRow(tripleTriad.TeachingItem?.Icon ?? 0);
        RowHeader(card, tripleTriad.Name, typeTag, tripleTriad.Stars > 0 ? $"{tripleTriad.Stars}-star" : "");

        TextLine($"Top {tripleTriad.Top} · Bottom {tripleTriad.Bottom} · Left {tripleTriad.Left} · Right {tripleTriad.Right}",
            plugin.Fonts.Body13, Theme.Neutral300);
    }

    private void DrawEmoteRow(EntityCardResult card, EmoteEntity emote)
    {
        RowHeader(card, emote.Name, Label("Emote", emote.Category));

        if (emote.Command.Length > 0)
            TextLine(emote.Command, plugin.Fonts.Body13, Theme.Neutral400);
    }

    private void DrawVistaRow(EntityCardResult card, VistaEntity vista)
    {
        RowHeader(card, vista.Name, Label("Vista", vista.Region));

        if (vista.Location is { } loc)
            PinnedLocationLine(loc.ZoneName, loc, $"vista{vista.RowId}");

        CollapsedSummary(card, vista.Hint);
    }

    // Hunt marks carry a rank and (for B ranks only) a zone; spawn points
    // aren't in the game data, so the row says where to look instead.
    private void DrawHuntMarkRow(EntityCardResult card, HuntMarkEntity mark)
    {
        RowHeader(card, mark.Name, "Hunt mark", mark.Rank.Length > 0 ? $"Rank {mark.Rank}" : "");

        if (mark.ZoneName.Length > 0)
        {
            using var font = plugin.Fonts.Body13.Push();
            GlyphPrefix(FontAwesomeIcon.MapPin);
            ImGui.TextUnformatted(mark.ZoneName);
        }

        TextLine("Spawn points live on the wiki.", plugin.Fonts.Citation12, Theme.Neutral600);
    }

    private void DrawAetherCurrentRow(EntityCardResult card, AetherCurrentZoneEntity currents)
    {
        RowHeader(card, currents.Name, "Aether currents");

        var count = currents.QuestCurrents.Count;
        TextLine(count == 1
            ? "1 current from a quest · the rest sit in the open world"
            : $"{count} currents from quests · the rest sit in the open world",
            plugin.Fonts.Body13, Theme.Neutral400);
    }

    private void DrawFateRow(EntityCardResult card, FateEntity fate)
    {
        RowHeader(card, fate.Name, "FATE", fate.Level > 0 ? $"Level {fate.Level}" : "");

        if (fate.Description.Length > 0)
            TextLine(fate.Description, plugin.Fonts.Body13, Theme.Neutral400, wrap: true);
    }

    private void DrawLeveRow(EntityCardResult card, LeveEntity leve)
    {
        RowHeader(card, leve.Name, Label("Levequest", leve.Type),
            leve.Level > 0 ? Label($"Level {leve.Level}", leve.JobCategory) : "");

        if (leve.Levemete is { } loc)
        {
            var place = leve.IssuedAt.Length > 0 && leve.IssuedAt != loc.ZoneName
                ? $"Levemete: {leve.IssuedAt}"
                : "Levemete";
            PinnedLocationLine(place, loc, $"leve{leve.RowId}", withZone: true);
        }

        CollapsedSummary(card, leve.Description);
    }

    // A wiki search hit with no extracted sections: just a pointer to the page.
    private void DrawWikiPageRow(WikiPageResult wiki)
    {
        RowTitle(wiki.Title);
        RowTag("Wiki", TagStyle.Wiki);
        RowCitation(wiki.Source.Label);
        if (HeaderAction("Open", $"wiki{wiki.Title}"))
            BrowserOpener.Open(wiki.PageUrl);

        TextLine("Wiki page found", plugin.Fonts.Body13, Theme.Neutral400);
    }

    // Wiki sections that no game-data card absorbed (see ResultJoiner).
    private void DrawWikiSectionsRow(WikiSectionsResult wiki)
    {
        RowTitle(wiki.Title);
        RowTag("Wiki", TagStyle.Wiki);
        RowCitation(wiki.Source.Label);
        if (HeaderAction("Open in browser", $"wikisec{wiki.Title}"))
            BrowserOpener.Open(wiki.PageUrl);

        ImGui.Spacing();
        DrawSectionList(wiki.Title, wiki.Sections);
    }

    // Collapsible headings with the cleaned section text beneath; used by
    // wiki rows and by the "FROM THE WIKI" block on game-data cards.
    private void DrawSectionList(string pageTitle, IReadOnlyList<WikiSectionText> sections)
    {
        var fonts = plugin.Fonts;

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            // A game-data card already answers the query, so wiki sections
            // stay folded behind their headings when one is present.
            var flags = i == 0 && !session.HasGameResult ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

            bool open;
            using (fonts.Body13.Push())
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Neutral400))
                open = ImGui.CollapsingHeader($"{section.Heading.ToUpperInvariant()}###sec{pageTitle}{i}", flags);

            if (open)
            {
                // PushIndent applies the global scale itself.
                using (ImRaii.PushIndent(Theme.Space4))
                    TextLine(section.Text, fonts.Section, Theme.Neutral300, wrap: true);
                ImGui.Spacing();
            }
        }
    }
}
