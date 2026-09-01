using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

public partial class MainWindow
{
    private const int MaxVendorLines = 6;
    private const int MaxNpcLocationLines = 6;

    // Matches the old provider cap, so the default view stays the same size
    // and the clipper-less list only grows when the user asks for the rest.
    private const int InitialRows = 8;

    // The game renders coordinates with a period on every client language;
    // CurrentCulture would show "11,7" on some.
    private static string Coords(float x, float y) =>
        string.Create(CultureInfo.InvariantCulture, $"{x:0.0}, {y:0.0}");

    private Vector2 headerMin;
    private Vector2 headerMax;
    private float headerRight;

    private void DrawResults(QueryResponse result)
    {
        if (result.Results.Count == 0)
        {
            plugin.Fonts.Body14.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextUnformatted($"Nothing found for \"{result.Query.Term}\".");
            ImGui.PopStyleColor();
            plugin.Fonts.Body14.Pop();
            DrawProviderFooter(result);
            return;
        }

        DrawCountStrip(result);
        ImGui.Spacing();

        var shown = Math.Min(InitialRows, Active.AboveGate.Count);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0)
                RowDivider();
            ImGui.PushID(i);
            DrawRow(Active.AboveGate[i], topRow: i == 0);
            ImGui.PopID();
        }

        if (Active.AboveGate.Count > InitialRows)
            DrawMoreStrip();

        if (Active.Wiki.Count > 0)
            DrawWikiStrip();

        if (Active.BelowGate.Count > 0)
            DrawLowRelevanceStrip();

        DrawProviderFooter(result);
    }

    private void DrawCountStrip(QueryResponse result)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        fonts.Small11.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted(string.Create(CultureInfo.InvariantCulture,
            $"{result.Results.Count} RESULTS · {result.Elapsed.TotalSeconds:0.00}S"));
        ImGui.PopStyleColor();

        foreach (var provider in result.ProviderDetail)
        {
            if (provider.Status == ProviderStatus.Ok && provider.Results.Count > 0)
            {
                ImGui.SameLine(0, Theme.Space2 * scale);
                // Provider pills share the 10px tag size; only the strip's
                // own labels run at 11px.
                fonts.Tag10.Push();
                Widgets.Tag($"{provider.ProviderId.Replace('-', ' ')} {provider.Results.Count}", TagStyle.Neutral);
                fonts.Tag10.Pop();
            }
        }

        var label = "RANKED BY SCORE";
        var width = ImGui.CalcTextSize(label).X;
        ImGui.SameLine(RowRightEdge() - width);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        fonts.Small11.Pop();
    }

    private static void RowDivider()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var height = MathF.Max(1f, scale);
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        ImGui.GetWindowDrawList().AddRectFilled(
            pos + new Vector2(Theme.Space4 * scale, 0),
            pos + new Vector2(width - (Theme.Space4 * scale), height),
            Theme.RowDividerU);
        ImGui.Dummy(new Vector2(0, height));
    }

    private void DrawRow(SearchResult hit, bool topRow)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var rowMin = ImGui.GetCursorScreenPos();
        var contentRight = rowMin.X + ImGui.GetContentRegionAvail().X;

        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(rowMin + (new Vector2(2f + Theme.Space4, Theme.Space3) * scale));
        ImGui.BeginGroup();
        headerRight = RowRightEdge();
        var known = DrawRowContent(hit);
        if (hit is EntityCardResult detailCard && HasDetail(detailCard) && Active.ExpandedRows.Contains(RowKey(detailCard)))
            DrawCardDetail(detailCard);
        ImGui.EndGroup();

        var rowMax = new Vector2(contentRight, ImGui.GetItemRectMax().Y + (Theme.Space3 * scale));
        var hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, rowMax);

        dl.ChannelsSetCurrent(0);
        if (hovered)
            dl.AddRectFilled(rowMin, rowMax, Theme.Accent900U);

        var mark = known ? (topRow ? Theme.AccentU : Theme.Accent700U) : Theme.Neutral800U;
        dl.AddRectFilled(rowMin, new Vector2(rowMin.X + (2f * scale), rowMax.Y), mark, 2f * scale);
        dl.ChannelsMerge();

        // The hover tint reads as clickable, so the row itself toggles its
        // detail; once expanded only the header line does, keeping clicks on
        // sections and buttons inside the detail from collapsing it.
        if (hovered && hit is EntityCardResult toggleCard && HasDetail(toggleCard))
        {
            var key = RowKey(toggleCard);
            var zoneMax = Active.ExpandedRows.Contains(key)
                ? new Vector2(contentRight, headerMax.Y + (Theme.Space2 * scale))
                : rowMax;
            if (ImGui.IsMouseHoveringRect(rowMin, zoneMax))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (!ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive() &&
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
                    !Active.ExpandedRows.Remove(key))
                    Active.ExpandedRows.Add(key);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));
        ImGui.Dummy(Vector2.Zero);
    }

    private bool DrawRowContent(SearchResult hit)
    {
        switch (hit)
        {
            case EntityCardResult { Entity: NpcEntity npc } card:
                DrawNpcRow(card, npc);
                return true;
            case EntityCardResult { Entity: ItemEntity item } card:
                DrawItemRow(card, item);
                return true;
            case EntityCardResult { Entity: QuestEntity quest } card:
                DrawQuestRow(card, quest);
                return true;
            case EntityCardResult { Entity: DutyEntity duty } card:
                DrawDutyRow(card, duty);
                return true;
            case EntityCardResult { Entity: AchievementEntity achievement } card:
                DrawAchievementRow(card, achievement);
                return true;
            case EntityCardResult { Entity: MountEntity mount } card:
                DrawIconTitleRow(card, "Mount", mount.Icon, mount.Description);
                return true;
            case EntityCardResult { Entity: MinionEntity minion } card:
                DrawIconTitleRow(card, "Minion", minion.Icon, minion.Description);
                return true;
            case EntityCardResult { Entity: OrchestrionEntity orchestrion } card:
                DrawIconTitleRow(card,
                    orchestrion.Category.Length > 0 ? $"Orchestrion · {orchestrion.Category}" : "Orchestrion",
                    orchestrion.TeachingItem?.Icon ?? 0, orchestrion.Description);
                return true;
            case EntityCardResult { Entity: TripleTriadCardEntity tripleTriad } card:
                DrawTripleTriadRow(card, tripleTriad);
                return true;
            case EntityCardResult { Entity: EmoteEntity emote } card:
                DrawEmoteRow(card, emote);
                return true;
            case EntityCardResult { Entity: VistaEntity vista } card:
                DrawVistaRow(card, vista);
                return true;
            case EntityCardResult { Entity: HuntMarkEntity mark } card:
                DrawHuntMarkRow(card, mark);
                return true;
            case EntityCardResult { Entity: AetherCurrentZoneEntity currents } card:
                DrawAetherCurrentRow(card, currents);
                return true;
            case EntityCardResult { Entity: FateEntity fate } card:
                DrawFateRow(card, fate);
                return true;
            case EntityCardResult { Entity: LeveEntity leve } card:
                DrawLeveRow(card, leve);
                return true;
            case WikiPageResult wiki:
                DrawWikiPageRow(wiki);
                return true;
            case WikiSectionsResult sections:
                DrawWikiSectionsRow(sections);
                return true;
            default:
                RowTitle(hit.Title);
                RowTag(hit.Source.Label, TagStyle.Neutral);
                return false;
        }
    }

    private void DrawNpcRow(EntityCardResult card, NpcEntity npc)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(npc.Name);
        RowTag("NPC", TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        var locations = card.MergedCount > 1
            ? card.MergedLocations
            : npc.Location is { } single ? [single] : (IReadOnlyList<MapLocation>)[];

        if (card.MergedCount > 1)
        {
            fonts.Small11.Push();
            var note = $"{card.MergedCount} ROWS MERGED · {locations.Count} WITH A LOCATION";
            ImGui.SameLine();
            ImGui.SetCursorPosX(headerRight - ImGui.CalcTextSize(note).X);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted(note);
            ImGui.PopStyleColor();
            fonts.Small11.Pop();
        }

        // Expanded cards show the same list inside the Locations tab instead,
        // so the row draws it only while collapsed.
        if (ShowNpcTabs(card) && Active.ExpandedRows.Contains(RowKey(card)))
            return;

        DrawNpcLocations(npc.RowId, locations, card.MergedHidden);
    }

    private void DrawNpcLocations(uint npcRowId, IReadOnlyList<MapLocation> locations, int hidden)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        var shown = locations.Count;
        if (plugin.Configuration.CapNpcLocationPins && locations.Count > MaxNpcLocationLines)
            shown = MaxNpcLocationLines;

        for (var i = 0; i < shown; i++)
        {
            var loc = locations[i];
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(loc.ZoneName);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted(Coords(loc.MapX, loc.MapY));
            ImGui.PopStyleColor();
            DrawAetheryteTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            DrawLocationActions(loc, $"flag{npcRowId}-{i}");
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        if (shown < locations.Count)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"+{locations.Count - shown} more locations");
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }

        if (hidden > 0)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"{hidden} duplicate rows hidden.");
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }
    }

    private void DrawItemRow(EntityCardResult card, ItemEntity item)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        DrawIconTile(item.Icon);
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.BeginGroup();
        RowTitle(item.Name);
        RowTag(item.Category.Length > 0 ? $"Item · {item.Category}" : "Item", TagStyle.Accent);
        if (plugin.Configuration.ShowEquipmentStats && item.Equipment is { } gear)
            RowTag($"ilvl {gear.ItemLevel} · Lv. {gear.EquipLevel}", TagStyle.Outline);
        if (plugin.Configuration.ShowItemUsage && item.Usage is { MateriaTag.Length: > 0 } materia)
            RowTag(materia.MateriaTag, TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (item.Description.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            Widgets.TextEllipsized(item.Description, ImGui.GetContentRegionAvail().X - (Theme.Space4 * scale));
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }

        fonts.Citation12.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted(item.Marketable ? "Marketboard: yes" : "Marketboard: no");
        ImGui.PopStyleColor();
        fonts.Citation12.Pop();
        ImGui.EndGroup();
    }

    private void DrawQuestRow(EntityCardResult card, QuestEntity quest)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

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
            RowTag(quest.Genre.Length > 0 ? $"Quest · {quest.Genre}" : "Quest", TagStyle.Accent);
        }

        if (quest.ClassJobLevel > 0)
            RowTag($"Level {quest.ClassJobLevel}", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (quest.StartLocation is { } start && !msqHidden)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted($"Starts: {start.ZoneName}");
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted(Coords(start.MapX, start.MapY));
            ImGui.PopStyleColor();
            DrawAetheryteTail(start, LocationActionsWidth(start));
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            DrawLocationActions(start, $"queststart{quest.RowId}");
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        // The expanded card shows the full chain, so the one-line summary
        // only appears while collapsed.
        var chainShown = plugin.Configuration.ShowUnlockRequirements && Active.ExpandedRows.Contains(RowKey(card));
        if (quest.Prerequisites.Count > 0 && !chainShown && !msqHidden)
        {
            var joiner = quest.PrerequisiteJoin == QuestJoin.Any ? " or " : ", ";
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped("Requires: " + string.Join(joiner, quest.Prerequisites.Select(p => PrerequisiteLabel(quest, p))));
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
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

    private void DrawDutyRow(EntityCardResult card, DutyEntity duty)
    {
        var gated = IsSpoilerGated(card);
        if (gated)
            BlurredRowTitle(duty.Name);
        else
            RowTitle(duty.Name);

        var kind = duty.FieldArea ? "Area" : "Duty";
        RowTag(duty.ContentType.Length > 0 ? $"{kind} · {duty.ContentType}" : kind, TagStyle.Accent);
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
            plugin.Fonts.Citation12.Push();
            if (Widgets.GhostButton($"Show content from beyond MSQ progress - may contain spoilers##reveal{duty.RowId}"))
                revealedSpoilers.Add(RowKey(card));
            plugin.Fonts.Citation12.Pop();
        }
    }

    private void DrawAchievementRow(EntityCardResult card, AchievementEntity achievement)
    {
        var fonts = plugin.Fonts;

        RowTitle(achievement.Name);
        RowTag(achievement.Category.Length > 0 ? $"Achievement · {achievement.Category}" : "Achievement",
            TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (achievement.Description.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped(achievement.Description);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawIconTitleRow(EntityCardResult card, string tag, ushort icon, string description = "")
    {
        var scale = ImGuiHelpers.GlobalScale;

        DrawIconTile(icon);
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.BeginGroup();
        RowTitle(card.Entity.Name);
        RowTag(tag, TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        // The expanded detail shows the full text, so the ellipsized line
        // only appears while collapsed.
        if (description.Length > 0 && !Active.ExpandedRows.Contains(RowKey(card)))
        {
            plugin.Fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            Widgets.TextEllipsized(description, ImGui.GetContentRegionAvail().X - (Theme.Space4 * scale));
            ImGui.PopStyleColor();
            plugin.Fonts.Body13.Pop();
        }

        ImGui.EndGroup();
    }

    private void DrawTripleTriadRow(EntityCardResult card, TripleTriadCardEntity tripleTriad)
    {
        var fonts = plugin.Fonts;

        DrawIconTile(tripleTriad.TeachingItem?.Icon ?? 0);
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        ImGui.BeginGroup();
        RowTitle(tripleTriad.Name);
        RowTag(tripleTriad.CardType.Length > 0
            ? $"Triple Triad · {tripleTriad.CardType}"
            : "Triple Triad card", TagStyle.Accent);
        if (tripleTriad.Stars > 0)
            RowTag($"{tripleTriad.Stars}-star", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        ImGui.TextUnformatted(
            $"Top {tripleTriad.Top} · Bottom {tripleTriad.Bottom} · Left {tripleTriad.Left} · Right {tripleTriad.Right}");
        ImGui.PopStyleColor();
        fonts.Body13.Pop();
        ImGui.EndGroup();
    }

    private void DrawEmoteRow(EntityCardResult card, EmoteEntity emote)
    {
        var fonts = plugin.Fonts;

        RowTitle(emote.Name);
        RowTag(emote.Category.Length > 0 ? $"Emote · {emote.Category}" : "Emote", TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (emote.Command.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextUnformatted(emote.Command);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawVistaRow(EntityCardResult card, VistaEntity vista)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(vista.Name);
        RowTag(vista.Region.Length > 0 ? $"Vista · {vista.Region}" : "Vista", TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (vista.Location is { } loc)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(loc.ZoneName);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted(Coords(loc.MapX, loc.MapY));
            ImGui.PopStyleColor();
            DrawAetheryteTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            DrawLocationActions(loc, $"vista{vista.RowId}");
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        // The expanded detail shows the full hint, so the ellipsized line only
        // appears while collapsed.
        if (vista.Hint.Length > 0 && !Active.ExpandedRows.Contains(RowKey(card)))
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            Widgets.TextEllipsized(vista.Hint, ImGui.GetContentRegionAvail().X - (Theme.Space4 * scale));
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawHuntMarkRow(EntityCardResult card, HuntMarkEntity mark)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(mark.Name);
        RowTag("Hunt mark", TagStyle.Accent);
        if (mark.Rank.Length > 0)
            RowTag($"Rank {mark.Rank}", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (mark.ZoneName.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(mark.ZoneName);
            fonts.Body13.Pop();
        }

        fonts.Citation12.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted("Spawn points live on the wiki.");
        ImGui.PopStyleColor();
        fonts.Citation12.Pop();
    }

    private void DrawAetherCurrentRow(EntityCardResult card, AetherCurrentZoneEntity currents)
    {
        var fonts = plugin.Fonts;

        RowTitle(currents.Name);
        RowTag("Aether currents", TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        var count = currents.QuestCurrents.Count;
        ImGui.TextUnformatted(count == 1
            ? "1 current from a quest · the rest sit in the open world"
            : $"{count} currents from quests · the rest sit in the open world");
        ImGui.PopStyleColor();
        fonts.Body13.Pop();
    }

    private void DrawFateRow(EntityCardResult card, FateEntity fate)
    {
        var fonts = plugin.Fonts;

        RowTitle(fate.Name);
        RowTag("FATE", TagStyle.Accent);
        if (fate.Level > 0)
            RowTag($"Level {fate.Level}", TagStyle.Outline);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (fate.Description.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped(fate.Description);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawLeveRow(EntityCardResult card, LeveEntity leve)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(leve.Name);
        RowTag(leve.Type.Length > 0 ? $"Levequest · {leve.Type}" : "Levequest", TagStyle.Accent);
        if (leve.Level > 0)
        {
            RowTag(leve.JobCategory.Length > 0
                ? $"Level {leve.Level} · {leve.JobCategory}"
                : $"Level {leve.Level}", TagStyle.Outline);
        }

        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (leve.Levemete is { } loc)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            var place = leve.IssuedAt.Length > 0 && leve.IssuedAt != loc.ZoneName
                ? $"Levemete: {leve.IssuedAt}"
                : "Levemete";
            ImGui.TextUnformatted(place);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"{loc.ZoneName} {Coords(loc.MapX, loc.MapY)}");
            ImGui.PopStyleColor();
            DrawAetheryteTail(loc, LocationActionsWidth(loc));
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            DrawLocationActions(loc, $"leve{leve.RowId}");
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        // The expanded detail shows the full brief, so the ellipsized line
        // only appears while collapsed.
        if (leve.Description.Length > 0 && !Active.ExpandedRows.Contains(RowKey(card)))
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            Widgets.TextEllipsized(leve.Description, ImGui.GetContentRegionAvail().X - (Theme.Space4 * scale));
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawWikiPageRow(WikiPageResult wiki)
    {
        var fonts = plugin.Fonts;

        RowTitle(wiki.Title);
        RowTag("Wiki", TagStyle.Wiki);
        RowCitation(wiki.Source.Label);
        if (HeaderAction("Open", $"wiki{wiki.Title}"))
            BrowserOpener.Open(wiki.PageUrl);

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        ImGui.TextUnformatted("Wiki page found");
        ImGui.PopStyleColor();
        fonts.Body13.Pop();
    }

    private void DrawWikiSectionsRow(WikiSectionsResult wiki)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(wiki.Title);
        RowTag("Wiki", TagStyle.Wiki);
        RowCitation(wiki.Source.Label);
        if (HeaderAction("Open in browser", $"wikisec{wiki.Title}"))
            BrowserOpener.Open(wiki.PageUrl);

        ImGui.Spacing();
        DrawSectionList(wiki.Title, wiki.Sections);
    }

    private void DrawSectionList(string pageTitle, IReadOnlyList<WikiSectionText> sections)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            // A game-data card already answers the query, so wiki sections
            // stay folded behind their headings when one is present.
            var flags = i == 0 && !Active.HasGameResult ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            var open = ImGui.CollapsingHeader($"{section.Heading.ToUpperInvariant()}###sec{pageTitle}{i}", flags);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();

            if (open)
            {
                ImGui.Indent(Theme.Space4 * scale);
                fonts.Section.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
                ImGui.TextWrapped(section.Text);
                ImGui.PopStyleColor();
                fonts.Section.Pop();
                ImGui.Unindent(Theme.Space4 * scale);
                ImGui.Spacing();
            }
        }
    }

    private bool ToggleStrip(string id, string label, bool open)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Spacing();
        var height = 26f * scale;
        var clicked = ImGui.InvisibleButton(id, new Vector2(ImGui.GetContentRegionAvail().X, height));

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, ImGui.IsItemHovered() ? Theme.Accent900U : Theme.SurfaceU,
            Theme.RadiusMd * scale);

        fonts.Small11.Push();
        var caret = (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        var lineHeight = ImGui.GetTextLineHeight();
        var textY = min.Y + ((height - lineHeight) * 0.5f);
        var x = min.X + (Theme.Space4 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral500U, caret);
        x += ImGui.CalcTextSize(caret).X + (Theme.Space3 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral400U, label);
        fonts.Small11.Pop();

        return clicked;
    }

    private void DrawMoreStrip()
    {
        var hidden = Active.AboveGate.Count - InitialRows;
        if (ToggleStrip("##wikiway-more", $"{hidden} MORE RESULTS", Active.MoreOpen))
            Active.MoreOpen = !Active.MoreOpen;

        if (Active.MoreOpen)
        {
            for (var i = InitialRows; i < Active.AboveGate.Count; i++)
            {
                RowDivider();
                ImGui.PushID(i);
                DrawRow(Active.AboveGate[i], topRow: false);
                ImGui.PopID();
            }
        }
    }

    private void DrawWikiStrip()
    {
        if (ToggleStrip("##wikiway-wiki", $"{Active.Wiki.Count} WIKI RESULTS", Active.WikiOpen))
            Active.WikiOpen = !Active.WikiOpen;

        if (Active.WikiOpen)
        {
            for (var i = 0; i < Active.Wiki.Count; i++)
            {
                RowDivider();
                // Offset past both gate lists so the three lists never collide.
                ImGui.PushID(2000 + i);
                DrawRow(Active.Wiki[i], topRow: false);
                ImGui.PopID();
            }
        }
    }

    private void DrawLowRelevanceStrip()
    {
        if (ToggleStrip("##wikiway-lowrelevance", $"{Active.BelowGate.Count} LOW CONFIDENCE RESULTS",
                Active.LowRelevanceOpen))
            Active.LowRelevanceOpen = !Active.LowRelevanceOpen;

        if (Active.LowRelevanceOpen)
        {
            for (var i = 0; i < Active.BelowGate.Count; i++)
            {
                RowDivider();
                // Offset past the above-gate IDs so the two lists never collide.
                ImGui.PushID(1000 + i);
                DrawRow(Active.BelowGate[i], topRow: false);
                ImGui.PopID();
            }
        }
    }

    private void DrawProviderFooter(QueryResponse result)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var first = true;

        foreach (var provider in result.ProviderDetail)
        {
            if (provider.Status != ProviderStatus.Failed)
                continue;

            if (first)
            {
                ImGui.Spacing();
                Widgets.FadingRule();
                ImGui.Spacing();
                first = false;
            }

            var pos = ImGui.GetCursorScreenPos();
            fonts.Citation12.Push();
            var lineHeight = ImGui.GetTextLineHeight();
            ImGui.GetWindowDrawList().AddRectFilled(
                pos + new Vector2(0, (lineHeight - (12f * scale)) * 0.5f),
                pos + new Vector2(2f * scale, (lineHeight + (12f * scale)) * 0.5f),
                Theme.Neutral600U, 2f * scale);
            ImGui.SetCursorScreenPos(pos + new Vector2((2f + Theme.Space3) * scale, 0));
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextUnformatted($"{provider.ProviderId} unavailable ({provider.Error}) - results may be incomplete.");
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            if (Widgets.OutlinedButton($"Retry##retry{provider.ProviderId}"))
                RunQuery(Active, Categories[categoryIndex].Value);
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }
    }

    private void RowTitle(string title)
    {
        plugin.Fonts.Title17.Push();
        ImGui.TextUnformatted(title);
        plugin.Fonts.Title17.Pop();
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    private void BlurredRowTitle(string title)
    {
        plugin.Fonts.Title17.Push();
        Widgets.BlurredText(title);
        plugin.Fonts.Title17.Pop();
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    private void RowTag(string text, TagStyle style)
    {
        var handle = plugin.Fonts.Tag10;
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        handle.Push();
        var pillHeight = ImGui.GetTextLineHeight() + (4f * ImGuiHelpers.GlobalScale);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMin.Y + ((headerMax.Y - headerMin.Y - pillHeight) * 0.5f) });
        Widgets.Tag(text, style);
        handle.Pop();
    }

    private void RowCitation(string source)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SameLine(0, Theme.Space3 * scale);
        plugin.Fonts.Citation12.Push();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMax.Y - ImGui.GetTextLineHeight() - (2f * scale) });
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
        ImGui.TextUnformatted(source);
        ImGui.PopStyleColor();
        plugin.Fonts.Citation12.Pop();
    }

    // SameLine(offset) adds the group offset inside a row group, so explicit
    // right-alignment goes through SetCursorPosX instead.
    private bool HeaderAction(string label, string id)
    {
        var scale = ImGuiHelpers.GlobalScale;
        plugin.Fonts.Citation12.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
        var width = ImGui.CalcTextSize(label).X + (Theme.Space4 * 2f * scale);
        ImGui.SameLine();
        ImGui.SetCursorPosX(headerRight - width);
        var buttonHeight = ImGui.GetTextLineHeight() + (Theme.Space2 * 2f * scale);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMin.Y + ((headerMax.Y - headerMin.Y - buttonHeight) * 0.5f) });
        var clicked = Widgets.OutlinedButton($"{label}##{id}");
        ImGui.PopStyleVar();
        plugin.Fonts.Citation12.Pop();
        headerRight -= width + (Theme.Space2 * scale);
        return clicked;
    }

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

    private bool GatingActive =>
        plugin.Configuration.SpoilerProtectionEnabled && plugin.QuestProgress.IsAvailable;

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

    private static string RowKey(EntityCardResult card) => $"{card.Entity.GetType().Name}:{card.Title}";

    private void RowDetailControls(EntityCardResult card)
    {
        if (!HasDetail(card))
            return;

        var key = RowKey(card);
        var open = Active.ExpandedRows.Contains(key);
        var caret = (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        if (HeaderAction(caret, $"expand{key}") && !Active.ExpandedRows.Remove(key))
            Active.ExpandedRows.Add(key);
    }

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

    // The per-source loops shared by the item card and every teaching-item reuse.
    private void DrawAcquisitionSources(uint ownerRowId, ItemAcquisition acquisition)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        foreach (var recipe in acquisition.Recipes)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Hammer.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(recipe.Level > 0 ? $"{recipe.CraftType} · Level {recipe.Level}" : recipe.CraftType);
            ImGui.PopStyleColor();
            if (recipe.MasterBook.Length > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted("· requires");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                if (Widgets.LinkText(recipe.MasterBook))
                    queuedNavigation = (recipe.MasterBook, SearchCategory.Items);
            }

            if (recipe.Ingredients.Count > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted("—");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                DrawItemAmounts(recipe.Ingredients);
            }

            fonts.Body13.Pop();
        }

        var shown = 0;
        foreach (var vendor in acquisition.Vendors)
        {
            if (shown == MaxVendorLines)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"+{acquisition.Vendors.Count - shown} more vendors");
                ImGui.PopStyleColor();
                fonts.Citation12.Pop();
                break;
            }

            shown++;
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(vendor.NpcName);
            var actionsWidth = LocationActionsWidth(vendor.Location);
            var price = vendor.GilPrice > 0 ? $"{vendor.GilPrice} gil" : "";
            var priceWidth = price.Length > 0 ? CitationTextWidth(price) + (Theme.Space3 * scale) : 0f;
            if (vendor.Location is { } loc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{loc.ZoneName} {Coords(loc.MapX, loc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(loc, actionsWidth + priceWidth);
            }

            fonts.Body13.Pop();

            fonts.Citation12.Push();
            if (price.Length > 0)
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(RowRightEdge() - actionsWidth - priceWidth);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
                ImGui.TextUnformatted(price);
                ImGui.PopStyleColor();
            }

            if (vendor.Location is { } flagLoc)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(flagLoc, $"vendor{ownerRowId}-{shown}");
                ImGui.PopStyleVar();
            }

            fonts.Citation12.Pop();
        }

        var exchangesShown = 0;
        foreach (var exchange in acquisition.Exchanges)
        {
            if (exchangesShown == MaxVendorLines)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"+{acquisition.Exchanges.Count - exchangesShown} more exchanges");
                ImGui.PopStyleColor();
                fonts.Citation12.Pop();
                break;
            }

            exchangesShown++;
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.ExchangeAlt.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(exchange.NpcName);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextUnformatted($"{exchange.ShopName} —");
            ImGui.PopStyleColor();
            for (var o = 0; o < exchange.Costs.Count; o++)
            {
                if (o > 0)
                {
                    ImGui.SameLine(0, 0);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                    ImGui.TextUnformatted(",");
                    ImGui.PopStyleColor();
                }

                ImGui.SameLine(0, Theme.Space2 * scale);
                DrawItemAmounts(exchange.Costs[o]);
            }

            if (exchange.Location is { } exchangeLoc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{exchangeLoc.ZoneName} {Coords(exchangeLoc.MapX, exchangeLoc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(exchangeLoc, LocationActionsWidth(exchangeLoc));
            }

            fonts.Body13.Pop();

            if (exchange.Location is { } exchangeFlagLoc)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(exchangeFlagLoc, $"exch{ownerRowId}-{exchangesShown}");
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }
        }

        var nodesShown = 0;
        foreach (var node in acquisition.Gathering)
        {
            if (nodesShown == MaxVendorLines)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"+{acquisition.Gathering.Count - nodesShown} more nodes");
                ImGui.PopStyleColor();
                fonts.Citation12.Pop();
                break;
            }

            nodesShown++;
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Leaf.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(node.Level > 0 ? $"{node.NodeType} · Level {node.Level}" : node.NodeType);
            ImGui.PopStyleColor();
            if (node.TimeWindow.Length > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
                ImGui.TextUnformatted(FontAwesomeIcon.Clock.ToIconString());
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
                ImGui.TextUnformatted(node.TimeWindow);
                ImGui.PopStyleColor();
            }

            if (node.Location is { } nodeLoc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{nodeLoc.ZoneName} {Coords(nodeLoc.MapX, nodeLoc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(nodeLoc, LocationActionsWidth(nodeLoc));
            }

            fonts.Body13.Pop();

            if (node.Location is { } nodeFlagLoc)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(nodeFlagLoc, $"gather{ownerRowId}-{nodesShown}");
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }
        }

        var spotsShown = 0;
        foreach (var spot in acquisition.Fishing)
        {
            if (spotsShown == MaxVendorLines)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"+{acquisition.Fishing.Count - spotsShown} more fishing spots");
                ImGui.PopStyleColor();
                fonts.Citation12.Pop();
                break;
            }

            spotsShown++;
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Fish.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            var method = spot.Spearfishing ? "Spearfishing" : "Fishing";
            ImGui.TextUnformatted(spot.Level > 0 ? $"{method} · Level {spot.Level}" : method);
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(spot.SpotName);
            if (spot.Location is { } spotLoc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{spotLoc.ZoneName} {Coords(spotLoc.MapX, spotLoc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(spotLoc, LocationActionsWidth(spotLoc));
            }

            fonts.Body13.Pop();

            if (spot.Location is { } spotFlagLoc)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(spotFlagLoc, $"fish{ownerRowId}-{spotsShown}");
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }
        }

        if (acquisition.FishingNote.Length > 0)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextWrapped(acquisition.FishingNote);
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }

        var sealVendorsShown = 0;
        foreach (var sealVendor in acquisition.SealVendors)
        {
            sealVendorsShown++;
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Medal.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(sealVendor.NpcName);
            if (sealVendor.RequiredRank.Length > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted($"requires {sealVendor.RequiredRank}");
                ImGui.PopStyleColor();
            }

            var sealActionsWidth = LocationActionsWidth(sealVendor.Location);
            var seals = $"{sealVendor.SealCost} seals";
            var sealsWidth = CitationTextWidth(seals) + (Theme.Space3 * scale);
            if (sealVendor.Location is { } sealLoc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{sealLoc.ZoneName} {Coords(sealLoc.MapX, sealLoc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(sealLoc, sealActionsWidth + sealsWidth);
            }

            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - sealActionsWidth - sealsWidth);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextUnformatted(seals);
            ImGui.PopStyleColor();

            if (sealVendor.Location is { } sealFlagLoc)
            {
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(sealFlagLoc, $"seal{ownerRowId}-{sealVendorsShown}");
                ImGui.PopStyleVar();
            }

            fonts.Citation12.Pop();
        }

        foreach (var venture in acquisition.Ventures)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Suitcase.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted($"Retainer venture · Level {venture.Level} {venture.Category}");
            ImGui.PopStyleColor();
            if (venture.Quantities.Length > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted($"— {venture.Quantities} per venture");
                ImGui.PopStyleColor();
            }

            fonts.Body13.Pop();
        }
    }

    private void DrawTrailingDetail(EntityCardResult card)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

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
                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                var width = ImGui.CalcTextSize("Open").X + (Theme.Space4 * 2f * scale);
                ImGui.SameLine();
                ImGui.SetCursorPosX(RowRightEdge() - width);
                if (Widgets.OutlinedButton($"Open##wikidetail{card.Title}"))
                    BrowserOpener.Open(url);
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }

            DrawSectionList(card.Title, card.WikiSections);
        }
    }

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

        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        if (card.Entity is MinionEntity { BattleStats: { } stats })
        {
            ImGui.Spacing();
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            var special = stats.SpecialAction.Length > 0 ? $" · Special: {stats.SpecialAction}" : "";
            ImGui.TextWrapped($"Lord of Verminion — HP {stats.Hp} · Attack {stats.Attack} · " +
                $"Defense {stats.Defense} · Speed {stats.Speed} · Cost {stats.Cost}{special}");
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
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
            {
                fonts.Body13.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
                ImGui.TextUnformatted(FontAwesomeIcon.Scroll.ToIconString());
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
                ImGui.TextUnformatted("Unlocked by quest");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                if (Widgets.LinkText(unlockQuest.Name))
                    queuedNavigation = (unlockQuest.Name, SearchCategory.Unlocks);
                fonts.Body13.Pop();
            }

            if (teaching != null)
            {
                fonts.Body13.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
                ImGui.TextUnformatted(FontAwesomeIcon.Book.ToIconString());
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
                ImGui.TextUnformatted(card.Entity is EmoteEntity ? "Unlocked by item" : "Taught by item");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                if (Widgets.LinkText(teaching.Name))
                    queuedNavigation = (teaching.Name, SearchCategory.Items);
                fonts.Body13.Pop();

                if (teaching.Acquisition is { } sources)
                    DrawAcquisitionSources(card.Entity.RowId, sources);
            }
        }

        if (flavor is { Length: > 0 })
        {
            ImGui.Spacing();
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextWrapped(flavor);
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }
    }

    private void DrawVistaDetail(VistaEntity vista)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("SIGHTSEEING LOG");

        fonts.Body13.Push();
        if (vista.Hint.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextWrapped(vista.Hint);
            ImGui.PopStyleColor();
        }

        if (vista.Emote.Length > 0)
            UsageLine(FontAwesomeIcon.Eye, $"Emote: {vista.Emote}");
        if (vista.TimeWindow.Length > 0)
            UsageLine(FontAwesomeIcon.Clock, vista.TimeWindow);
        fonts.Body13.Pop();

        if (vista.Lore.Length > 0)
        {
            ImGui.Spacing();
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextWrapped(vista.Lore);
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }
    }

    private void DrawAetherCurrentsDetail(AetherCurrentZoneEntity currents)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("UNLOCK QUESTS");

        var viewWidth = FlagButtonWidth("View");
        for (var i = 0; i < currents.QuestCurrents.Count; i++)
        {
            var step = currents.QuestCurrents[i];

            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(step.Quest.Name);
            ImGui.PopStyleColor();
            if (step.Level > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"Lv {step.Level}");
                ImGui.PopStyleColor();
            }

            StepCheckmark(step.Quest.RowId);
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            if (step.StartLocation is { } start)
                DrawLocationActions(start, $"currentflag{currents.RowId}-{i}", compact: true, reserved: viewWidth + (Theme.Space2 * scale));

            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - viewWidth);
            if (Widgets.OutlinedButton($"View##current{currents.RowId}-{i}"))
                queuedNavigation = (step.Quest.Name, SearchCategory.Unlocks);
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        fonts.Citation12.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted("Field currents are scattered in the open world - the wiki page lists them.");
        ImGui.PopStyleColor();
        fonts.Citation12.Pop();
    }

    private void DrawFateRequirement(QuestLink quest)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("REQUIREMENTS");

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.Scroll.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        ImGui.TextUnformatted("Requires quest");
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space2 * scale);
        if (Widgets.LinkText(quest.Name))
            queuedNavigation = (quest.Name, SearchCategory.Unlocks);
        fonts.Body13.Pop();
    }

    private void DrawLeveDetail(LeveEntity leve)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("LEVEQUEST");

        fonts.Body13.Push();
        if (leve.Description.Length > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextWrapped(leve.Description);
            ImGui.PopStyleColor();
        }

        var reward = leve.AllowanceCost == 1 ? "1 leve allowance" : $"{leve.AllowanceCost} leve allowances";
        if (leve.ExpReward > 0)
            reward += $" · {leve.ExpReward} exp";
        if (leve.GilReward > 0)
            reward += $" · {leve.GilReward} gil";
        UsageLine(FontAwesomeIcon.Scroll, reward);
        fonts.Body13.Pop();
    }

    private void DrawCardObtain(TripleTriadCardEntity tripleTriad)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        if (tripleTriad.NpcName.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted($"Won from {tripleTriad.NpcName}");
            if (tripleTriad.NpcLocation is { } loc)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"{loc.ZoneName} {Coords(loc.MapX, loc.MapY)}");
                ImGui.PopStyleColor();
                DrawAetheryteTail(loc, LocationActionsWidth(loc));
            }

            fonts.Body13.Pop();

            if (tripleTriad.NpcLocation is { } flagLoc)
            {
                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                DrawLocationActions(flagLoc, $"ttnpc{tripleTriad.RowId}");
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }
        }

        if (tripleTriad.DutyName.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.Dungeon.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted($"Dropped in {tripleTriad.DutyName}");
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }

        if (tripleTriad.ObtainText.Length > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
            ImGui.TextUnformatted(FontAwesomeIcon.InfoCircle.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(tripleTriad.ObtainText);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    // Quantities stay plain text; the item name is the link, so "where do I
    // get the Steel Amalj'ok" is one click instead of a retyped search.
    private void DrawItemAmounts(IReadOnlyList<ItemAmount> parts)
    {
        var scale = ImGuiHelpers.GlobalScale;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine(0, Theme.Space2 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted("+");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextUnformatted($"{parts[i].Amount}x");
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space1 * scale);
            if (Widgets.LinkText(parts[i].Name))
                queuedNavigation = (parts[i].Name, SearchCategory.Items);
        }
    }

    private void DrawEquipment(ItemEquipment eq)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("EQUIPMENT");

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.ShieldAlt.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        ImGui.TextUnformatted(eq.Slot.Length > 0 ? $"{eq.Slot} · ilvl {eq.ItemLevel}" : $"ilvl {eq.ItemLevel}");
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        ImGui.TextUnformatted(eq.ClassJobs.Length > 0 ? $"Lv. {eq.EquipLevel} · {eq.ClassJobs}" : $"Lv. {eq.EquipLevel}");
        ImGui.PopStyleColor();

        var combat = new List<string>();
        if (eq.Weapon is { } weapon)
        {
            if (weapon.PhysDamage > 0)
                combat.Add($"Physical Damage {weapon.PhysDamage}{HqSuffix(weapon.HqPhysBonus)}");
            if (weapon.MagDamage > 0)
                combat.Add($"Magic Damage {weapon.MagDamage}{HqSuffix(weapon.HqMagBonus)}");
            combat.Add(string.Create(CultureInfo.InvariantCulture, $"Delay {weapon.DelaySeconds:0.00}s"));
        }

        if (eq.Defense is { } defense)
        {
            combat.Add($"Defense {defense.Physical}{HqSuffix(defense.HqPhysBonus)}");
            combat.Add($"Magic Defense {defense.Magical}{HqSuffix(defense.HqMagBonus)}");
        }

        if (eq.Block is { } block)
        {
            combat.Add($"Block {block.Strength}{HqSuffix(block.HqStrengthBonus)}");
            combat.Add($"Block Rate {block.Rate}{HqSuffix(block.HqRateBonus)}");
        }

        if (combat.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(string.Join(" · ", combat));
            ImGui.PopStyleColor();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        foreach (var stat in eq.Stats)
            ImGui.TextUnformatted($"{stat.Name} {(stat.Value >= 0 ? "+" : "")}{stat.Value}{HqSuffix(stat.HqBonus)}");
        ImGui.PopStyleColor();
        fonts.Body13.Pop();

        var meta = new List<string>();
        if (eq.MateriaSlots > 0)
            meta.Add($"{eq.MateriaSlots} materia slot{(eq.MateriaSlots > 1 ? "s" : "")}{(eq.AdvancedMelding ? " (advanced melding)" : "")}");
        if (eq.DyeCount > 0)
            meta.Add(eq.DyeCount > 1 ? $"Dyeable ×{eq.DyeCount}" : "Dyeable");
        if (eq.Unique)
            meta.Add("Unique");
        if (eq.Untradable)
            meta.Add("Untradable");
        if (eq.CanBeHq)
            meta.Add("HQ available");
        if (eq.Repair.Length > 0)
            meta.Add($"Repair: {eq.Repair}");
        if (eq.Desynthable)
            meta.Add("Desynthable");
        if (eq.SellPrice > 0 && !eq.Untradable)
            meta.Add($"Sells for {eq.SellPrice} gil");
        if (eq.Series.Length > 0)
            meta.Add($"Set: {eq.Series}");
        if (eq.SpecialBonus.Length > 0)
            meta.Add(eq.SpecialBonus);

        if (meta.Count > 0)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextWrapped(string.Join(" · ", meta));
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }
    }

    private static string HqSuffix(int bonus) => bonus > 0 ? $" (+{bonus} HQ)" : "";

    private void DrawConsumable(ItemFoodEffect food)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("CONSUMABLE");

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.Utensils.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        var header = food.StatusName.Length > 0
            ? $"{food.StatusName} · {DurationLabel(food.DurationSeconds)}"
            : DurationLabel(food.DurationSeconds);
        if (food.ExpBonusPercent > 0)
            header += $" · EXP +{food.ExpBonusPercent}%";
        ImGui.TextUnformatted(header);

        foreach (var stat in food.Stats)
            ImGui.TextUnformatted(FoodStatLine(stat));
        ImGui.PopStyleColor();
        fonts.Body13.Pop();
    }

    private static string DurationLabel(int seconds) =>
        seconds >= 60 && seconds % 60 == 0 ? $"{seconds / 60} min" : $"{seconds}s";

    private static string FoodStatLine(FoodStat stat)
    {
        var line = $"{stat.Name} {FoodValue(stat.Relative, stat.Value, stat.Max)}";
        if (stat.HqValue != stat.Value || stat.HqMax != stat.Max)
            line += $" (HQ {FoodValue(stat.Relative, stat.HqValue, stat.HqMax)})";
        return line;
    }

    private static string FoodValue(bool relative, int value, int max)
    {
        var sign = value >= 0 ? "+" : "";
        return relative ? $"{sign}{value}% (max {max})" : $"{sign}{value}";
    }

    private void DrawUsage(ItemUsage usage)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("USAGE");

        fonts.Body13.Push();
        if (usage.MateriaTag.Length > 0)
            UsageLine(FontAwesomeIcon.Gem, $"Materia · {usage.MateriaTag}");

        if (usage.UsedInRecipes > 0)
            UsageLine(FontAwesomeIcon.Hammer,
                usage.UsedInRecipes == 1 ? "Used in 1 recipe" : $"Used in {usage.UsedInRecipes} recipes");

        foreach (var delivery in usage.Deliveries)
        {
            UsageLine(FontAwesomeIcon.BoxOpen, $"Custom delivery · {delivery.NpcName}");
            if (delivery.UnlockQuest is { } quest)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted("— requires");
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space2 * scale);
                if (Widgets.LinkText(quest.Name))
                    queuedNavigation = (quest.Name, SearchCategory.Unlocks);
            }
        }

        foreach (var turnIn in usage.CollectableTurnIns)
        {
            var band = turnIn.LevelMax > turnIn.LevelMin
                ? $"Lv {turnIn.LevelMin}-{turnIn.LevelMax}"
                : $"Lv {turnIn.LevelMin}";
            UsageLine(FontAwesomeIcon.Star, $"Collectable turn-in · {band}");
            if (turnIn.MaxScrips > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                ImGui.TextUnformatted($"— up to {turnIn.MaxScrips} scrips");
                ImGui.PopStyleColor();
            }
        }

        if (usage.TreasureMap is { } map)
        {
            var sites = map.Zones.Sum(z => z.SpotCount);
            var party = map.PartySize > 1 ? $" · up to {map.PartySize} players" : "";
            UsageLine(FontAwesomeIcon.Map, $"Treasure map{party} · {sites} dig sites");
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
            ImGui.TextWrapped(string.Join(", ", map.Zones.Select(z => $"{z.ZoneName} ({z.SpotCount})")));
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }

        fonts.Body13.Pop();
    }

    private void UsageLine(FontAwesomeIcon icon, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(icon.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private void DrawNpcTabs(EntityCardResult card, NpcEntity npc)
    {
        var fonts = plugin.Fonts;

        var locations = card.MergedCount > 1
            ? card.MergedLocations
            : npc.Location is { } single ? [single] : (IReadOnlyList<MapLocation>)[];

        ImGui.Spacing();
        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Tab, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, Theme.Accent900);
        ImGui.PushStyleColor(ImGuiCol.TabActive, Theme.Accent800);
        if (ImGui.BeginTabBar($"##npctabs{npc.RowId}"))
        {
            if (ImGui.BeginTabItem($"Locations##npcloc{npc.RowId}"))
            {
                DrawNpcLocations(npc.RowId, locations, card.MergedHidden);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"Cutscene appearances##npccs{npc.RowId}"))
            {
                DrawSceneAppearances(card, npc);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.PopStyleColor(3);
        fonts.Body13.Pop();
    }

    private void DrawSceneAppearances(EntityCardResult card, NpcEntity npc)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var viewWidth = FlagButtonWidth("View");

        if (!Active.SceneGroups.TryGetValue(RowKey(card), out var groups))
            return;

        foreach (var group in groups)
        {
            var key = $"{npc.RowId}:{group.Order}";
            var open = Active.ExpandedScenes.Contains(key);
            var caret = (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
            var count = group.Scenes.Count;
            var summary = count == 1 ? "1 appearance" : $"{count} appearances";

            fonts.Body13.Push();
            if (Widgets.GhostButton($"{caret} {group.Expansion} — {summary}##scenes{key}") &&
                !Active.ExpandedScenes.Remove(key))
                Active.ExpandedScenes.Add(key);
            fonts.Body13.Pop();

            if (!open)
                continue;

            var i = 0;
            foreach (var appearance in group.Scenes)
            {
                i++;
                fonts.Body13.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
                ImGui.TextUnformatted(appearance.Quest.Name);
                ImGui.PopStyleColor();
                if (appearance.Location is { } loc)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                    ImGui.TextUnformatted($"{loc.ZoneName} {loc.MapX:0.0}, {loc.MapY:0.0}");
                    ImGui.PopStyleColor();
                }

                fonts.Body13.Pop();

                fonts.Citation12.Push();
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                if (appearance.Location is { } flagLoc)
                    DrawLocationActions(flagLoc, $"sceneflag{npc.RowId}-{group.Order}-{i}", compact: true, reserved: viewWidth + (Theme.Space2 * scale));

                ImGui.SameLine();
                ImGui.SetCursorPosX(RowRightEdge() - viewWidth);
                if (Widgets.OutlinedButton($"View##scene{npc.RowId}-{group.Order}-{i}"))
                    queuedNavigation = (appearance.Quest.Name, SearchCategory.Unlocks);
                ImGui.PopStyleVar();
                fonts.Citation12.Pop();
            }
        }
    }

    private void DrawUnlockChain(QuestEntity quest)
    {
        ImGui.Spacing();
        DetailLabel("UNLOCK REQUIREMENTS");

        for (var i = 0; i < quest.UnlockChains.Count; i++)
        {
            var chain = quest.UnlockChains[i];
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

    private void DrawChainBlock(QuestEntity quest, QuestChain chain, int index)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var progress = plugin.QuestProgress;

        var key = (quest.RowId, index);
        var open = Active.ExpandedChains.Contains(key);
        var caret = (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        var label = chain.Genre.Length > 0 ? chain.Genre : "Quest chain";
        var count = chain.Steps.Count == 1 ? "1 quest" : $"{chain.Steps.Count} quests";
        var summary = $"{caret} {label}: {count}";
        if (progress.IsAvailable)
            summary += $" ({chain.Steps.Count(s => progress.IsComplete(s.Quest.RowId))} done)";
        if (chain.Gate is { } gateSummary)
            summary += $" + Main Scenario ({gateSummary.Version})";
        if (chain.Join == QuestJoin.Any)
            summary += " (either)";

        fonts.Body13.Push();
        if (Widgets.GhostButton($"{summary}##chaintoggle{quest.RowId}-{index}") && !Active.ExpandedChains.Remove(key))
            Active.ExpandedChains.Add(key);
        fonts.Body13.Pop();

        if (!open)
            return;

        // Truncation drops the earliest quests, so the pointer to the rest
        // sits above the first listed step.
        if (chain.Continues)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted("chain continues - open the first quest to follow it further");
            ImGui.PopStyleColor();
            fonts.Citation12.Pop();
        }

        if (chain.Gate is { } gate)
            MsqLine(gate);

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

            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"{j + 1}.");
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
            ImGui.TextUnformatted(step.Quest.Name);
            ImGui.PopStyleColor();
            if (step.Level > 0)
            {
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                ImGui.TextUnformatted($"Lv {step.Level}");
                ImGui.PopStyleColor();
            }

            StepCheckmark(step.Quest.RowId);
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            if (j == nextIdx && step.StartLocation is { } start)
                DrawLocationActions(start, $"chainflag{quest.RowId}-{index}-{j}", compact: true, reserved: viewWidth + (Theme.Space2 * scale));

            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - viewWidth);
            if (Widgets.OutlinedButton($"View##chain{quest.RowId}-{index}-{j}"))
                queuedNavigation = (step.Quest.Name, SearchCategory.Unlocks);
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }
    }

    private void MsqLine(MsqGate gate)
    {
        var fonts = plugin.Fonts;
        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.Lock.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        ImGui.TextUnformatted($"Main Scenario ({gate.Version})");
        ImGui.PopStyleColor();
        StepCheckmark(gate.Quest.RowId);
        fonts.Body13.Pop();
    }

    private void StepCheckmark(uint questRowId)
    {
        if (!plugin.QuestProgress.IsComplete(questRowId))
            return;

        ImGui.SameLine(0, Theme.Space2 * ImGuiHelpers.GlobalScale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.Check.ToIconString());
        ImGui.PopStyleColor();
    }

    private void DrawDutyUnlock(DutyEntity duty)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        DetailLabel("UNLOCK REQUIREMENTS");

        // A main-scenario unlock quest's name stays withheld even after the
        // card itself is revealed - the reveal consents to the duty, not to
        // printing story titles.
        var msqUnlock = duty.MsqGate is { } gate
            && gate.Quest.RowId == duty.UnlockQuest!.RowId
            && IsMsqTitleHidden(gate.Quest.RowId);

        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.TextUnformatted(FontAwesomeIcon.Lock.ToIconString());
        ImGui.PopStyleColor();
        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral300);
        if (msqUnlock)
        {
            ImGui.TextUnformatted("Unlocked by quest:");
            ImGui.SameLine(0, Theme.Space2 * scale);
            Widgets.BlurredText(duty.UnlockQuest!.Name);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"Main Scenario ({duty.MsqGate!.Version})");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextUnformatted($"Unlocked by quest: {duty.UnlockQuest!.Name}");
        }

        ImGui.PopStyleColor();
        fonts.Body13.Pop();

        if (msqUnlock)
            return;

        fonts.Citation12.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
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
        ImGui.PopStyleVar();
        fonts.Citation12.Pop();
    }

    private void DetailLabel(string text)
    {
        plugin.Fonts.Tag10.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        plugin.Fonts.Tag10.Pop();
    }

    private static float RowRightEdge() =>
        ImGui.GetWindowContentRegionMax().X - (Theme.Space4 * ImGuiHelpers.GlobalScale);

    private float FlagButtonWidth(string label)
    {
        plugin.Fonts.Citation12.Push();
        var width = ImGui.CalcTextSize(label).X + (Theme.Space4 * 2f * ImGuiHelpers.GlobalScale);
        plugin.Fonts.Citation12.Pop();
        return width;
    }

    private float CitationTextWidth(string text)
    {
        plugin.Fonts.Citation12.Push();
        var width = ImGui.CalcTextSize(text).X;
        plugin.Fonts.Citation12.Pop();
        return width;
    }

    private bool CanTeleport(MapLocation? location) =>
        location?.Aetheryte is { TeleportRowId: not 0 } near
        && plugin.Configuration.TeleportButtonEnabled
        && plugin.Teleport.CanTeleportTo(near.TeleportRowId);

    // Width of a location line's right-aligned action cluster: the flag plus,
    // when a teleport provider can reach the location's aetheryte, Teleport.
    // Null locations still reserve the flag width so price columns line up.
    private float LocationActionsWidth(MapLocation? location, bool compact = false)
    {
        var width = FlagButtonWidth(Widgets.FlagLabel(compact ? "Flag" : "Flag map"));
        if (CanTeleport(location))
            width += FlagButtonWidth(Widgets.TeleportLabel(compact ? "" : "Teleport")) + (Theme.Space2 * ImGuiHelpers.GlobalScale);
        return width;
    }

    // Draws [Teleport] [Flag] right-aligned, leaving `reserved` free to the
    // right for whatever the caller draws after (a View button). The caller
    // holds the Citation12 font and the compact FramePadding.
    private void DrawLocationActions(MapLocation location, string id, bool compact = false, float reserved = 0f)
    {
        ImGui.SameLine();
        ImGui.SetCursorPosX(RowRightEdge() - reserved - LocationActionsWidth(location, compact));
        if (CanTeleport(location) && location.Aetheryte is { } near)
        {
            if (Widgets.TeleportButton(compact ? "" : "Teleport", $"tp{id}"))
                plugin.Teleport.TeleportTo(near.TeleportRowId);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Teleport to {near.TeleportName}");
            ImGui.SameLine(0, Theme.Space2 * ImGuiHelpers.GlobalScale);
        }

        if (Widgets.FlagButton(compact ? "Flag" : "Flag map", id))
            MapLinkOpener.Open(location);
    }

    // Muted "· Aetheryte: X" after a location's coordinates, in the caller's
    // font. Skipped when it would run under the right-aligned cluster; the
    // caller's next SameLine re-anchors on the coordinates either way.
    private void DrawAetheryteTail(MapLocation location, float reservedRight)
    {
        if (!plugin.Configuration.ShowNearestAetheryte || location.Aetheryte is not { } near)
            return;

        var scale = ImGuiHelpers.GlobalScale;
        var text = $"· {(near.Aethernet ? "Aethernet" : "Aetheryte")}: {near.Name}";
        ImGui.SameLine(0, Theme.Space3 * scale);
        if (ImGui.GetCursorPosX() + ImGui.CalcTextSize(text).X > RowRightEdge() - reservedRight - (Theme.Space3 * scale))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    private static void DrawIconTile(ushort icon)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = new Vector2(34f, 34f) * scale;
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var rounding = Theme.RadiusSm * scale;

        dl.AddRectFilled(pos, pos + size, Theme.SurfaceU, rounding);
        if (icon > 0)
        {
            var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
            dl.AddImageRounded(wrap.Handle, pos, pos + size, Vector2.Zero, Vector2.One, 0xFFFFFFFF, rounding);
        }

        dl.AddRect(pos, pos + size, Theme.Neutral800U, rounding);
        ImGui.Dummy(size);
    }
}
