using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// Small drawing pieces every result row and detail block is built from: the
// header line (title, tags, citation, header buttons), labelled text lines,
// links, and the shared widths that keep right-aligned buttons in columns.
public partial class MainWindow
{
    // Bounds of the current row's title, recorded by RowTitle so the tags,
    // citation and header buttons that follow can center on it. headerRight
    // moves left as each header button claims space.
    private Vector2 headerMin;
    private Vector2 headerMax;
    private float headerRight;

    private void RowTitle(string title)
    {
        plugin.Fonts.Title17.Push();
        ImGui.TextUnformatted(title);
        plugin.Fonts.Title17.Pop();
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    // Spoiler-gated variant: same footprint, unreadable text.
    private void BlurredRowTitle(string title)
    {
        plugin.Fonts.Title17.Push();
        Widgets.BlurredText(title);
        plugin.Fonts.Title17.Pop();
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    // A tag pill after the title, vertically centered on it.
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

    // Where the row came from ("Game data", "consolegameswiki"), baseline-aligned with the title.
    private void RowCitation(string source)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SameLine(0, Theme.Space3 * scale);
        plugin.Fonts.Citation12.Push();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMax.Y - ImGui.GetTextLineHeight() - (2f * scale) });
        ColorText(source, Theme.Neutral500);
        plugin.Fonts.Citation12.Pop();
    }

    // A button at the right end of the header line (Open, the expand caret).
    // SameLine(offset) adds the group offset inside a row group, so explicit
    // right-alignment goes through SetCursorPosX instead.
    private bool HeaderAction(string label, string id)
    {
        var scale = ImGuiHelpers.GlobalScale;
        PushRowButtonStyle();
        var width = ImGui.CalcTextSize(label).X + (Theme.Space4 * 2f * scale);
        ImGui.SameLine();
        ImGui.SetCursorPosX(headerRight - width);
        var buttonHeight = ImGui.GetTextLineHeight() + (Theme.Space2 * 2f * scale);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMin.Y + ((headerMax.Y - headerMin.Y - buttonHeight) * 0.5f) });
        var clicked = Widgets.OutlinedButton($"{label}##{id}");
        PopRowButtonStyle();
        headerRight -= width + (Theme.Space2 * scale);
        return clicked;
    }

    // Small caps heading that opens a detail block ("ACQUISITION", "USAGE").
    private void DetailLabel(string text) => TextLine(text, plugin.Fonts.Tag10, Theme.Neutral600);

    // Citation12 with compact padding: the look of every in-row button.
    private void PushRowButtonStyle()
    {
        plugin.Fonts.Citation12.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * ImGuiHelpers.GlobalScale);
    }

    private void PopRowButtonStyle()
    {
        ImGui.PopStyleVar();
        plugin.Fonts.Citation12.Pop();
    }

    private static void ColorText(string text, Vector4 color, bool wrap = false)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        if (wrap)
            ImGui.TextWrapped(text);
        else
            ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    // Text in its own font and color, on its own line.
    private static void TextLine(string text, IFontHandle font, Vector4 color, bool wrap = false)
    {
        font.Push();
        ColorText(text, color, wrap);
        font.Pop();
    }

    // Text continuing the current line after a gap, in the caller's font.
    private static void InlineText(string text, Vector4 color, float gap = Theme.Space3)
    {
        ImGui.SameLine(0, gap * ImGuiHelpers.GlobalScale);
        ColorText(text, color);
    }

    // A collapsed row's one-line summary, cut with an ellipsis; the expanded
    // detail shows the full text.
    private void EllipsizedLine(string text)
    {
        plugin.Fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        Widgets.TextEllipsized(text, ImGui.GetContentRegionAvail().X - (Theme.Space4 * ImGuiHelpers.GlobalScale));
        ImGui.PopStyleColor();
        plugin.Fonts.Body13.Pop();
    }

    // The highlighted glyph that starts most detail lines.
    private static void GlyphPrefix(FontAwesomeIcon icon)
    {
        ColorText(icon.ToIconString(), Theme.Highlight);
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
    }

    // Glyph + label: the standard detail line, in the caller's font.
    private static void IconLine(FontAwesomeIcon icon, string text)
    {
        GlyphPrefix(icon);
        ColorText(text, Theme.Neutral300);
    }

    // IconLine as a line of its own in the body font.
    private void DetailLine(FontAwesomeIcon icon, string text)
    {
        plugin.Fonts.Body13.Push();
        IconLine(icon, text);
        plugin.Fonts.Body13.Pop();
    }

    // Names inside detail lines (ingredients, quests, teaching items) are
    // links that run a new search. The search is queued, not run, because
    // the result list is still being drawn.
    private void NavLink(string name, SearchCategory category)
    {
        if (Widgets.LinkText(name))
            queuedNavigation = (name, category);
    }

    // Glyph + label + link, e.g. "Requires quest <name>".
    private void LinkLine(FontAwesomeIcon icon, string label, string name, SearchCategory category)
    {
        plugin.Fonts.Body13.Push();
        IconLine(icon, label);
        ImGui.SameLine(0, Theme.Space2 * ImGuiHelpers.GlobalScale);
        NavLink(name, category);
        plugin.Fonts.Body13.Pop();
    }

    private static string CaretGlyph(bool open) =>
        (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();

    // The "View" button that ends quest-step rows (chains, aether currents,
    // cutscene appearances). The caller holds the row button style.
    private static bool RightViewButton(string id, float viewWidth)
    {
        ImGui.SameLine();
        ImGui.SetCursorPosX(RowRightEdge() - viewWidth);
        return Widgets.OutlinedButton($"View##{id}");
    }

    private static float RowRightEdge() =>
        ImGui.GetWindowContentRegionMax().X - (Theme.Space4 * ImGuiHelpers.GlobalScale);

    // Width of an outlined row button with this label, for right-alignment.
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

    // The game's icon for items, mounts and minions, in a rounded 34px tile.
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
