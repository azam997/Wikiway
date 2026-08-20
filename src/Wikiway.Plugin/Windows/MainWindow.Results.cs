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

        for (var i = 0; i < Active.AboveGate.Count; i++)
        {
            if (i > 0)
                RowDivider();
            ImGui.PushID(i);
            DrawRow(Active.AboveGate[i], topRow: i == 0);
            ImGui.PopID();
        }

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
                DrawIconTitleRow(card, "Mount", mount.Icon);
                return true;
            case EntityCardResult { Entity: MinionEntity minion } card:
                DrawIconTitleRow(card, "Minion", minion.Icon);
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

        var flagWidth = FlagButtonWidth(Widgets.FlagLabel("Flag map"));
        for (var i = 0; i < locations.Count; i++)
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
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - flagWidth);
            if (Widgets.FlagButton("Flag map", $"flag{npcRowId}-{i}"))
                MapLinkOpener.Open(loc);
            ImGui.PopStyleVar();
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
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - FlagButtonWidth(Widgets.FlagLabel("Flag map")));
            if (Widgets.FlagButton("Flag map", $"queststart{quest.RowId}"))
                MapLinkOpener.Open(start);
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

    private void DrawIconTitleRow(EntityCardResult card, string tag, ushort icon)
    {
        DrawIconTile(icon);
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        ImGui.BeginGroup();
        RowTitle(card.Entity.Name);
        RowTag(tag, TagStyle.Accent);
        RowCitation(card.Source.Label);
        RowDetailControls(card);
        ImGui.EndGroup();
    }

    private void DrawWikiPageRow(WikiPageResult wiki)
    {
        var fonts = plugin.Fonts;

        RowTitle(wiki.Title);
        RowTag("Wiki page", TagStyle.Neutral);
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
        RowTag("Wiki sections", TagStyle.Neutral);
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

    private void DrawLowRelevanceStrip()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();

        ImGui.Spacing();
        var height = 26f * scale;
        if (ImGui.InvisibleButton("##wikiway-lowrelevance", new Vector2(ImGui.GetContentRegionAvail().X, height)))
            Active.LowRelevanceOpen = !Active.LowRelevanceOpen;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, ImGui.IsItemHovered() ? Theme.Accent900U : Theme.SurfaceU,
            Theme.RadiusMd * scale);

        fonts.Small11.Push();
        var caret = (Active.LowRelevanceOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        var lineHeight = ImGui.GetTextLineHeight();
        var textY = min.Y + ((height - lineHeight) * 0.5f);
        var x = min.X + (Theme.Space4 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral500U, caret);
        x += ImGui.CalcTextSize(caret).X + (Theme.Space3 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral400U, $"{Active.BelowGate.Count} LOW CONFIDENCE RESULTS");
        fonts.Small11.Pop();

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
            || ShowNpcTabs(card)
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
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        if (card.Entity is NpcEntity npcEntity && ShowNpcTabs(card))
            DrawNpcTabs(card, npcEntity);

        if (card.Entity is ItemEntity { Acquisition: { } acquisition })
        {
            ImGui.Spacing();
            DetailLabel("ACQUISITION");

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
                if (recipe.Ingredients.Count > 0)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
                    ImGui.TextUnformatted("— " + string.Join(", ", recipe.Ingredients));
                    ImGui.PopStyleColor();
                }

                fonts.Body13.Pop();
            }

            var flagWidth = FlagButtonWidth(Widgets.FlagLabel("Flag map"));
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
                if (vendor.Location is { } loc)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                    ImGui.TextUnformatted($"{loc.ZoneName} {Coords(loc.MapX, loc.MapY)}");
                    ImGui.PopStyleColor();
                }

                fonts.Body13.Pop();

                fonts.Citation12.Push();
                if (vendor.GilPrice > 0)
                {
                    var price = $"{vendor.GilPrice} gil";
                    var priceWidth = ImGui.CalcTextSize(price).X;
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(RowRightEdge() - flagWidth - (Theme.Space3 * scale) - priceWidth);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
                    ImGui.TextUnformatted(price);
                    ImGui.PopStyleColor();
                }

                if (vendor.Location is { } flagLoc)
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(RowRightEdge() - flagWidth);
                    if (Widgets.FlagButton("Flag map", $"vendor{card.Entity.RowId}-{shown}"))
                        MapLinkOpener.Open(flagLoc);
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
                ImGui.TextUnformatted($"{exchange.ShopName} — {string.Join(", ", exchange.Costs)}");
                ImGui.PopStyleColor();
                if (exchange.Location is { } exchangeLoc)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                    ImGui.TextUnformatted($"{exchangeLoc.ZoneName} {Coords(exchangeLoc.MapX, exchangeLoc.MapY)}");
                    ImGui.PopStyleColor();
                }

                fonts.Body13.Pop();

                if (exchange.Location is { } exchangeFlagLoc)
                {
                    fonts.Citation12.Push();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(RowRightEdge() - flagWidth);
                    if (Widgets.FlagButton("Flag map", $"exch{card.Entity.RowId}-{exchangesShown}"))
                        MapLinkOpener.Open(exchangeFlagLoc);
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
                if (node.Location is { } nodeLoc)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                    ImGui.TextUnformatted($"{nodeLoc.ZoneName} {Coords(nodeLoc.MapX, nodeLoc.MapY)}");
                    ImGui.PopStyleColor();
                }

                fonts.Body13.Pop();

                if (node.Location is { } nodeFlagLoc)
                {
                    fonts.Citation12.Push();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(RowRightEdge() - flagWidth);
                    if (Widgets.FlagButton("Flag map", $"gather{card.Entity.RowId}-{nodesShown}"))
                        MapLinkOpener.Open(nodeFlagLoc);
                    ImGui.PopStyleVar();
                    fonts.Citation12.Pop();
                }
            }
        }

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
        var flagWidth = FlagButtonWidth(Widgets.FlagLabel("Flag"));

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
                {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(RowRightEdge() - viewWidth - (Theme.Space2 * scale) - flagWidth);
                    if (Widgets.FlagButton("Flag", $"sceneflag{npc.RowId}-{group.Order}-{i}"))
                        MapLinkOpener.Open(flagLoc);
                }

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
        var flagWidth = FlagButtonWidth(Widgets.FlagLabel("Flag"));
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
            {
                ImGui.SameLine();
                ImGui.SetCursorPosX(RowRightEdge() - viewWidth - (Theme.Space2 * scale) - flagWidth);
                if (Widgets.FlagButton("Flag", $"chainflag{quest.RowId}-{index}-{j}"))
                    MapLinkOpener.Open(start);
            }

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
