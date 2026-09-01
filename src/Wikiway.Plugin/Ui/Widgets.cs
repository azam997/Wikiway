using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;

namespace Wikiway.Plugin.Ui;

internal enum TagStyle
{
    Accent,
    Neutral,
    Outline,
    Wiki,
}

// Each helper draws one widget or primitive in the current font; layout stays
// with the caller.
internal static class Widgets
{
    public static bool GhostButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPressed);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static bool OutlinedButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPressed);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Accent);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
        return clicked;
    }

    // Map actions use the Highlight color and carry a glyph; measure widths
    // against the same label helpers so right-alignment stays exact.
    public static string FlagLabel(string text) =>
        $"{FontAwesomeIcon.Flag.ToIconString()} {text}";

    // Empty text gives the icon-only form used on compact rows.
    public static string TeleportLabel(string text) =>
        text.Length == 0
            ? FontAwesomeIcon.LocationArrow.ToIconString()
            : $"{FontAwesomeIcon.LocationArrow.ToIconString()} {text}";

    public static bool FlagButton(string text, string id) =>
        HighlightButton($"{FlagLabel(text)}##{id}");

    public static bool TeleportButton(string text, string id) =>
        HighlightButton($"{TeleportLabel(text)}##{id}");

    private static bool HighlightButton(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.HighlightHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.HighlightPressed);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Highlight);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Highlight);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(5);
        return clicked;
    }

    // Inline hyperlink text: hover shows an underline and hand cursor, click
    // returns true. Text items carry no ImGui id, so rows' IsAnyItemHovered
    // click guards don't see these - keep them out of header toggle zones.
    public static bool LinkText(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent300);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
        if (!ImGui.IsItemHovered())
            return false;

        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(min with { Y = max.Y }, max, Theme.Accent300U);
        return ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    public static void Tag(string text, TagStyle style)
    {
        var label = text.ToUpperInvariant();
        var scale = ImGuiHelpers.GlobalScale;
        var pad = new Vector2(6f, 2f) * scale;
        var size = ImGui.CalcTextSize(label) + (pad * 2);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        var rounding = 6f * scale;

        if (style == TagStyle.Outline)
            dl.AddRect(pos, pos + size, Theme.AccentU, rounding);
        else
            dl.AddRectFilled(pos, pos + size, style switch
            {
                TagStyle.Accent => Theme.Accent800U,
                TagStyle.Wiki => Theme.Highlight800U,
                _ => Theme.Neutral800U,
            }, rounding);

        dl.AddText(pos + pad, style switch
        {
            TagStyle.Accent => Theme.Accent100U,
            TagStyle.Outline => Theme.Accent300U,
            TagStyle.Wiki => Theme.Highlight100U,
            _ => Theme.Neutral100U,
        }, label);

        ImGui.Dummy(size);
    }

    public static void FadingRule()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fade = 48f * scale;
        var height = MathF.Max(1f, scale);
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var dl = ImGui.GetWindowDrawList();
        var transparent = Theme.DividerU & 0x00FFFFFF;

        if (width > fade * 2)
        {
            dl.AddRectFilledMultiColor(pos, pos + new Vector2(fade, height),
                transparent, Theme.DividerU, Theme.DividerU, transparent);
            dl.AddRectFilled(pos + new Vector2(fade, 0), pos + new Vector2(width - fade, height), Theme.DividerU);
            dl.AddRectFilledMultiColor(pos + new Vector2(width - fade, 0), pos + new Vector2(width, height),
                Theme.DividerU, transparent, transparent, Theme.DividerU);
        }
        else
        {
            dl.AddRectFilled(pos, pos + new Vector2(width, height), Theme.DividerU);
        }

        ImGui.Dummy(new Vector2(0, height));
    }

    public static void Spinner()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var radius = 5f * scale;
        var center = ImGui.GetCursorScreenPos() + new Vector2(radius, radius);
        var start = (float)ImGui.GetTime() * 6f;
        var dl = ImGui.GetWindowDrawList();
        dl.PathArcTo(center, radius, start, start + 4.7f);
        dl.PathStroke(Theme.AccentU, ImDrawFlags.None, 1.5f * scale);
        ImGui.Dummy(new Vector2(radius * 2, radius * 2));
    }

    public static void BlurredText(string text)
    {
        var scale = ImGuiHelpers.GlobalScale;
        var size = ImGui.CalcTextSize(text);
        var pos = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // Smudged draw-list passes plus a veil, reserved with a Dummy: no text
        // item exists, so hover, tooltip, and copy can never surface the string.
        var ink = ImGui.GetColorU32(Theme.Neutral300 with { W = 0.16f });
        for (var i = 0; i < 8; i++)
        {
            var angle = MathF.PI * 2f * i / 8f;
            dl.AddText(pos + (new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.6f * scale), ink, text);
        }

        dl.AddText(pos + new Vector2(3f * scale, 0), ink, text);
        dl.AddText(pos - new Vector2(3f * scale, 0), ink, text);
        dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(Theme.Bg with { W = 0.35f }));
        ImGui.Dummy(size);
    }

    public static void TextEllipsized(string text, float maxWidth)
    {
        text = text.Replace('\n', ' ');
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            ImGui.TextUnformatted(text);
            return;
        }

        var budget = maxWidth - ImGui.CalcTextSize("…").X;
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= budget)
                low = mid;
            else
                high = mid - 1;
        }

        // The binary search works in UTF-16 units; cutting between a surrogate
        // pair would hand ImGui a lone half.
        if (low > 0 && char.IsHighSurrogate(text[low - 1]))
            low--;

        ImGui.TextUnformatted(text[..low] + "…");
    }
}
