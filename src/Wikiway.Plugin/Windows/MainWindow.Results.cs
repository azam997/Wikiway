using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// The result list: count strip, ranked rows (each drawn by the per-kind
// methods in MainWindow.Rows.cs), the fold-out strips for extra, wiki and
// low-confidence rows, and the provider-failure footer.
public partial class MainWindow
{
    // Rows shown before the "N MORE RESULTS" strip. The list doesn't use
    // ImGui's clipper, so it stays short unless the user opens the rest.
    private const int InitialRows = 8;

    private void DrawResults(QueryResponse result)
    {
        if (result.Results.Count == 0)
        {
            TextLine($"Nothing found for \"{result.Query.Term}\".", plugin.Fonts.Body14, Theme.Neutral400);
            DrawProviderFooter(result);
            return;
        }

        DrawCountStrip(result);
        ImGui.Spacing();

        var shown = Math.Min(InitialRows, session.AboveGate.Count);
        for (var i = 0; i < shown; i++)
        {
            if (i > 0)
                RowDivider();
            using (ImRaii.PushId(i))
                DrawRow(session.AboveGate[i], topRow: i == 0);
        }

        if (session.AboveGate.Count > InitialRows)
            DrawMoreStrip();

        if (session.Wiki.Count > 0)
            DrawWikiStrip();

        if (session.BelowGate.Count > 0)
            DrawLowRelevanceStrip();

        DrawProviderFooter(result);
    }

    // "12 RESULTS · 0.42S" plus one pill per provider that returned rows.
    private void DrawCountStrip(QueryResponse result)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        using var font = fonts.Small11.Push();
        ColorText(string.Create(CultureInfo.InvariantCulture,
            $"{result.Results.Count} RESULTS · {result.Elapsed.TotalSeconds:0.00}S"), Theme.Neutral600);

        foreach (var provider in result.ProviderDetail)
        {
            if (provider.Status == ProviderStatus.Ok && provider.Results.Count > 0)
            {
                ImGui.SameLine(0, Theme.Space2 * scale);
                // Provider pills share the 10px tag size; only the strip's
                // own labels run at 11px.
                using (fonts.Tag10.Push())
                    Widgets.Tag($"{provider.ProviderId.Replace('-', ' ')} {provider.Results.Count}", TagStyle.Neutral);
            }
        }

        var label = "RANKED BY SCORE";
        var width = ImGui.CalcTextSize(label).X;
        ImGui.SameLine(RowRightEdge() - width);
        ColorText(label, Theme.Neutral600);
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

    // One result: content in a group, then the hover tint and the left
    // accent bar drawn behind it (channel 0) once its height is known.
    private void DrawRow(SearchResult hit, bool topRow)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var dl = ImGui.GetWindowDrawList();
        var rowMin = ImGui.GetCursorScreenPos();
        var contentRight = rowMin.X + ImGui.GetContentRegionAvail().X;
        Vector2 rowMax;
        bool hovered;

        // ImRaii has no scope for draw-list channels, so the finally keeps a
        // row that throws mid-draw from leaving the window's list split.
        dl.ChannelsSplit(2);
        try
        {
            dl.ChannelsSetCurrent(1);

            ImGui.SetCursorScreenPos(rowMin + ImGuiHelpers.ScaledVector2(2f + Theme.Space4, Theme.Space3));
            bool known;
            using (ImRaii.Group())
            {
                headerRight = RowRightEdge();
                known = DrawRowContent(hit);
                if (hit is EntityCardResult detailCard && HasDetail(detailCard) && session.ExpandedRows.Contains(RowKey(detailCard)))
                    DrawCardDetail(detailCard);
            }

            rowMax = new Vector2(contentRight, ImGui.GetItemRectMax().Y + (Theme.Space3 * scale));
            hovered = ImGui.IsWindowHovered() && ImGui.IsMouseHoveringRect(rowMin, rowMax);

            dl.ChannelsSetCurrent(0);
            if (hovered)
                dl.AddRectFilled(rowMin, rowMax, Theme.Accent900U);

            var mark = known ? (topRow ? Theme.AccentU : Theme.Accent700U) : Theme.Neutral800U;
            dl.AddRectFilled(rowMin, new Vector2(rowMin.X + (2f * scale), rowMax.Y), mark, 2f * scale);
        }
        finally
        {
            dl.ChannelsMerge();
        }

        // The hover tint reads as clickable, so the row itself toggles its
        // detail; once expanded only the header line does, keeping clicks on
        // sections and buttons inside the detail from collapsing it.
        if (hovered && hit is EntityCardResult toggleCard && HasDetail(toggleCard))
        {
            var key = RowKey(toggleCard);
            var zoneMax = session.ExpandedRows.Contains(key)
                ? new Vector2(contentRight, headerMax.Y + (Theme.Space2 * scale))
                : rowMax;
            if (ImGui.IsMouseHoveringRect(rowMin, zoneMax))
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (!ImGui.IsAnyItemHovered() && !ImGui.IsAnyItemActive() &&
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
                    !session.ExpandedRows.Remove(key))
                    session.ExpandedRows.Add(key);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(rowMin.X, rowMax.Y));
        ImGui.Dummy(Vector2.Zero);
    }

    // Dispatches to the per-kind row. False means an unrecognized result type
    // (drawn as a bare title), which gets the muted accent bar.
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
                DrawIconTitleRow(card, Label("Orchestrion", orchestrion.Category),
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

    // Full-width clickable bar ("3 MORE RESULTS") that folds a list open or shut.
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

        using (fonts.Small11.Push())
        {
            var caret = CaretGlyph(open);
            var lineHeight = ImGui.GetTextLineHeight();
            var textY = min.Y + ((height - lineHeight) * 0.5f);
            var x = min.X + (Theme.Space4 * scale);
            dl.AddText(new Vector2(x, textY), Theme.Neutral500U, caret);
            x += ImGui.CalcTextSize(caret).X + (Theme.Space3 * scale);
            dl.AddText(new Vector2(x, textY), Theme.Neutral400U, label);
        }

        return clicked;
    }

    private void DrawMoreStrip()
    {
        var hidden = session.AboveGate.Count - InitialRows;
        if (ToggleStrip("##wikiway-more", $"{hidden} MORE RESULTS", session.MoreOpen))
            session.MoreOpen = !session.MoreOpen;

        if (session.MoreOpen)
            DrawRowList(session.AboveGate, InitialRows, idOffset: 0);
    }

    private void DrawWikiStrip()
    {
        if (ToggleStrip("##wikiway-wiki", $"{session.Wiki.Count} WIKI RESULTS", session.WikiOpen))
            session.WikiOpen = !session.WikiOpen;

        // Offset past both gate lists so the three lists never collide.
        if (session.WikiOpen)
            DrawRowList(session.Wiki, 0, idOffset: 2000);
    }

    private void DrawLowRelevanceStrip()
    {
        if (ToggleStrip("##wikiway-lowrelevance", $"{session.BelowGate.Count} LOW CONFIDENCE RESULTS",
                session.LowRelevanceOpen))
            session.LowRelevanceOpen = !session.LowRelevanceOpen;

        // Offset past the above-gate IDs so the two lists never collide.
        if (session.LowRelevanceOpen)
            DrawRowList(session.BelowGate, 0, idOffset: 1000);
    }

    // Rows under a strip, each after a divider. ImGui ids are index-based, so
    // every list drawn in one frame needs its own offset.
    private void DrawRowList(List<SearchResult> rows, int from, int idOffset)
    {
        for (var i = from; i < rows.Count; i++)
        {
            RowDivider();
            using (ImRaii.PushId(idOffset + i))
                DrawRow(rows[i], topRow: false);
        }
    }

    // One line per provider that failed this search (e.g. the wiki timed
    // out), with a Retry button that re-runs the whole query.
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
            using var font = fonts.Citation12.Push();
            var lineHeight = ImGui.GetTextLineHeight();
            ImGui.GetWindowDrawList().AddRectFilled(
                pos + new Vector2(0, (lineHeight - (12f * scale)) * 0.5f),
                pos + new Vector2(2f * scale, (lineHeight + (12f * scale)) * 0.5f),
                Theme.Neutral600U, 2f * scale);
            ImGui.SetCursorScreenPos(pos + new Vector2((2f + Theme.Space3) * scale, 0));
            ColorText($"{provider.ProviderId} unavailable ({provider.Error}) - results may be incomplete.", Theme.Neutral500);
            ImGui.SameLine(0, Theme.Space3 * scale);
            using (ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ButtonPadding))
            {
                if (Widgets.OutlinedButton($"Retry##retry{provider.ProviderId}"))
                    RunQuery();
            }
        }
    }
}
