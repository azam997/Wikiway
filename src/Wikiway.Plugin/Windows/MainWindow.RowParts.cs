using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
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
        using (plugin.Fonts.Title17.Push())
            ImGui.TextUnformatted(title);
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    // Spoiler-gated variant: same footprint, unreadable text.
    private void BlurredRowTitle(string title)
    {
        using (plugin.Fonts.Title17.Push())
            Widgets.BlurredText(title);
        headerMin = ImGui.GetItemRectMin();
        headerMax = ImGui.GetItemRectMax();
    }

    // A tag pill after the title, vertically centered on it.
    private void RowTag(string text, TagStyle style)
    {
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        using var font = plugin.Fonts.Tag10.Push();
        var pillHeight = ImGui.GetTextLineHeight() + (4f * ImGuiHelpers.GlobalScale);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMin.Y + ((headerMax.Y - headerMin.Y - pillHeight) * 0.5f) });
        Widgets.Tag(text, style);
    }

    // Where the row came from ("Game data", "consolegameswiki"), baseline-aligned with the title.
    private void RowCitation(string source)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SameLine(0, Theme.Space3 * scale);
        using var font = plugin.Fonts.Citation12.Push();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMax.Y - ImGui.GetTextLineHeight() - (2f * scale) });
        ColorText(source, Theme.Neutral500);
    }

    // A button at the right end of the header line (Open, the expand caret).
    // SameLine(offset) adds the group offset inside a row group, so explicit
    // right-alignment goes through SetCursorPosX instead.
    private bool HeaderAction(string label, string id)
    {
        var scale = ImGuiHelpers.GlobalScale;
        using var font = plugin.Fonts.Citation12.Push();
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, ButtonPadding);
        var width = ImGui.CalcTextSize(label).X + (Theme.Space4 * 2f * scale);
        ImGui.SameLine();
        ImGui.SetCursorPosX(headerRight - width);
        var buttonHeight = ImGui.GetTextLineHeight() + (Theme.Space2 * 2f * scale);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = headerMin.Y + ((headerMax.Y - headerMin.Y - buttonHeight) * 0.5f) });
        headerRight -= width + (Theme.Space2 * scale);
        return Widgets.OutlinedButton($"{label}##{id}");
    }

    // Small caps heading that opens a detail block ("ACQUISITION", "USAGE").
    private void DetailLabel(string text) => TextLine(text, plugin.Fonts.Tag10, Theme.Neutral600);

    // FramePadding for compact buttons. With the Citation12 font it is the
    // look of every in-row button; the search bar uses it at Body14.
    private static Vector2 ButtonPadding => ImGuiHelpers.ScaledVector2(Theme.Space4, Theme.Space2);

    private static void ColorText(string text, Vector4 color, bool wrap = false)
    {
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, color);
        if (wrap)
            ImGui.TextWrapped(text);
        else
            ImGui.TextUnformatted(text);
    }

    // Text in its own font and color, on its own line.
    private static void TextLine(string text, IFontHandle font, Vector4 color, bool wrap = false)
    {
        using (font.Push())
            ColorText(text, color, wrap);
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
        using var font = plugin.Fonts.Body13.Push();
        using var textColor = ImRaii.PushColor(ImGuiCol.Text, Theme.Neutral400);
        Widgets.TextEllipsized(text, ImGui.GetContentRegionAvail().X - (Theme.Space4 * ImGuiHelpers.GlobalScale));
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
        using (plugin.Fonts.Body13.Push())
            IconLine(icon, text);
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
        using var font = plugin.Fonts.Body13.Push();
        IconLine(icon, label);
        ImGui.SameLine(0, Theme.Space2 * ImGuiHelpers.GlobalScale);
        NavLink(name, category);
    }

    private static string CaretGlyph(bool open) =>
        (open ? FontAwesomeIcon.CaretDown : FontAwesomeIcon.CaretRight).ToIconString();

    // The "View" button that ends quest-step rows (chains, aether currents,
    // cutscene appearances). The caller holds the row button font and padding.
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
        using var font = plugin.Fonts.Citation12.Push();
        return ImGui.CalcTextSize(label).X + (Theme.Space4 * 2f * ImGuiHelpers.GlobalScale);
    }

    private float CitationTextWidth(string text)
    {
        using var font = plugin.Fonts.Citation12.Push();
        return ImGui.CalcTextSize(text).X;
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
