using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
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

        for (var i = 0; i < aboveGate.Count; i++)
        {
            if (i > 0)
                RowDivider();
            DrawRow(aboveGate[i], topRow: i == 0);
        }

        if (belowGate.Count > 0)
            DrawLowRelevanceStrip();

        DrawProviderFooter(result);
    }

    private void DrawCountStrip(QueryResponse result)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        fonts.Small11.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
        ImGui.TextUnformatted($"{result.Results.Count} RESULTS · {result.Elapsed.TotalSeconds:0.00}S");
        ImGui.PopStyleColor();

        foreach (var provider in result.ProviderDetail)
        {
            if (provider.Status == ProviderStatus.Ok && provider.Results.Count > 0)
            {
                ImGui.SameLine(0, Theme.Space2 * scale);
                Widgets.Tag($"{provider.ProviderId.Replace('-', ' ')} {provider.Results.Count}", TagStyle.Neutral);
            }
        }

        var label = "RANKED BY SCORE";
        var width = ImGui.CalcTextSize(label).X;
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - width);
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
        if (hit is EntityCardResult detailCard && HasDetail(detailCard) && expandedRows.Contains(RowKey(detailCard)))
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
            var zoneMax = expandedRows.Contains(key)
                ? new Vector2(contentRight, headerMax.Y + (Theme.Space2 * scale))
                : rowMax;
            if (ImGui.IsMouseHoveringRect(rowMin, zoneMax))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (!ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive() &&
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
                    !expandedRows.Remove(key))
                    expandedRows.Add(key);
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

        var flagWidth = FlagButtonWidth("Flag map");
        for (var i = 0; i < locations.Count; i++)
        {
            var loc = locations[i];
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
            ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
            ImGui.PopStyleColor();
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.TextUnformatted(loc.ZoneName);
            ImGui.SameLine(0, Theme.Space3 * scale);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"{loc.MapX:0.0}, {loc.MapY:0.0}");
            ImGui.PopStyleColor();
            fonts.Body13.Pop();

            fonts.Citation12.Push();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
            ImGui.SameLine();
            ImGui.SetCursorPosX(RowRightEdge() - flagWidth);
            if (Widgets.OutlinedButton($"Flag map##flag{npc.RowId}-{i}"))
                MapLinkOpener.Open(loc);
            ImGui.PopStyleVar();
            fonts.Citation12.Pop();
        }

        if (card.MergedHidden > 0)
        {
            fonts.Citation12.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted($"{card.MergedHidden} duplicate rows hidden.");
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
        var fonts = plugin.Fonts;

        RowTitle(quest.Name);
        RowTag(quest.Genre.Length > 0 ? $"Quest · {quest.Genre}" : "Quest", TagStyle.Accent);
        if (quest.ClassJobLevel > 0)
            RowTag($"Level {quest.ClassJobLevel}", TagStyle.Outline, plugin.Fonts.Small11);
        RowCitation(card.Source.Label);
        RowDetailControls(card);

        if (quest.Prerequisites.Count > 0)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped("Requires: " + string.Join(", ", quest.Prerequisites.Select(p => p.Name)));
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
    }

    private void DrawDutyRow(EntityCardResult card, DutyEntity duty)
    {
        RowTitle(duty.Name);
        RowTag(duty.ContentType.Length > 0 ? $"Duty · {duty.ContentType}" : "Duty", TagStyle.Accent);
        if (duty.ClassJobLevel > 0)
        {
            RowTag(duty.ItemLevel > 0
                ? $"Level {duty.ClassJobLevel} · ilvl {duty.ItemLevel}"
                : $"Level {duty.ClassJobLevel}", TagStyle.Outline, plugin.Fonts.Small11);
        }

        if (duty.HighEnd)
            RowTag("High-end", TagStyle.Accent, plugin.Fonts.Small11);
        if (duty.Solo)
            RowTag("Solo", TagStyle.Outline, plugin.Fonts.Small11);
        RowCitation(card.Source.Label);
        RowDetailControls(card);
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
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        RowTitle(wiki.Title);
        RowTag("Wiki page", TagStyle.Neutral);
        RowCitation(wiki.Source.Label);
        if (HeaderAction("Open", $"wiki{wiki.Title}"))
            BrowserOpener.Open(wiki.PageUrl);

        if (wiki.Lead != null)
        {
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped(wiki.Lead);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
        else if (wiki.Snippet != null)
        {
            fonts.Tag10.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted("SNIPPET");
            ImGui.PopStyleColor();
            fonts.Tag10.Pop();
            ImGui.SameLine(0, Theme.Space3 * scale);
            fonts.Body13.Push();
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
            ImGui.TextWrapped(wiki.Snippet);
            ImGui.PopStyleColor();
            fonts.Body13.Pop();
        }
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
            var flags = i == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;

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
            lowRelevanceOpen = !lowRelevanceOpen;

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        dl.AddRectFilled(min, max, ImGui.IsItemHovered() ? Theme.Accent900U : Theme.SurfaceU,
            Theme.RadiusMd * scale);

        fonts.Small11.Push();
        var caret = (lowRelevanceOpen ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        var lineHeight = ImGui.GetTextLineHeight();
        var textY = min.Y + ((height - lineHeight) * 0.5f);
        var x = min.X + (Theme.Space4 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral500U, caret);
        x += ImGui.CalcTextSize(caret).X + (Theme.Space3 * scale);
        dl.AddText(new Vector2(x, textY), Theme.Neutral400U, $"{belowGate.Count} HITS BELOW SCORE 0.2");
        fonts.Small11.Pop();

        if (lowRelevanceOpen)
        {
            foreach (var hit in belowGate)
            {
                RowDivider();
                DrawRow(hit, topRow: false);
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
                RunQuery();
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

    private void RowTag(string text, TagStyle style, IFontHandle? font = null)
    {
        var handle = font ?? plugin.Fonts.Tag10;
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

    private static bool HasDetail(EntityCardResult card) =>
        card.WikiSections.Count > 0 || card.Entity is ItemEntity { Acquisition: not null };

    private static string RowKey(EntityCardResult card) => $"{card.Entity.GetType().Name}:{card.Title}";

    private void RowDetailControls(EntityCardResult card)
    {
        if (!HasDetail(card))
            return;

        var key = RowKey(card);
        var open = expandedRows.Contains(key);
        var caret = (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();
        if (HeaderAction(caret, $"expand{key}") && !expandedRows.Remove(key))
            expandedRows.Add(key);
    }

    private void DrawCardDetail(EntityCardResult card)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        if (card.Entity is ItemEntity { Acquisition: { } acquisition })
        {
            ImGui.Spacing();
            DetailLabel("ACQUISITION");

            foreach (var recipe in acquisition.Recipes)
            {
                fonts.Body13.Push();
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
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

            var flagWidth = FlagButtonWidth("Flag map");
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
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
                ImGui.TextUnformatted(FontAwesomeIcon.MapPin.ToIconString());
                ImGui.PopStyleColor();
                ImGui.SameLine(0, Theme.Space3 * scale);
                ImGui.TextUnformatted(vendor.NpcName);
                if (vendor.Location is { } loc)
                {
                    ImGui.SameLine(0, Theme.Space3 * scale);
                    ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
                    ImGui.TextUnformatted($"{loc.ZoneName} {loc.MapX:0.0}, {loc.MapY:0.0}");
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
                    if (Widgets.OutlinedButton($"Flag map##vendor{card.Entity.RowId}-{shown}"))
                        MapLinkOpener.Open(flagLoc);
                    ImGui.PopStyleVar();
                }

                fonts.Citation12.Pop();
            }
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
