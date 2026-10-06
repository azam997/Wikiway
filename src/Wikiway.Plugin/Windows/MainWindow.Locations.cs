using System.Globalization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Wikiway.Core.Models;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// Location lines: coordinates, the nearest-aetheryte hint, and the
// [Teleport] [Flag map] buttons at the right edge of the row.
public partial class MainWindow
{
    // The game renders coordinates with a period on every client language;
    // CurrentCulture would show "11,7" on some.
    private static string Coords(float x, float y) =>
        string.Create(CultureInfo.InvariantCulture, $"{x:0.0}, {y:0.0}");

    // Pin glyph, label, coordinates, then the location buttons: NPC homes,
    // quest starts, vistas, levemetes. withZone adds the zone name before
    // the coordinates for labels that don't already name it.
    private void PinnedLocationLine(string label, MapLocation location, string id, bool withZone = false)
    {
        plugin.Fonts.Body13.Push();
        GlyphPrefix(FontAwesomeIcon.MapPin);
        ImGui.TextUnformatted(label);
        LocationTail(location, LocationActionsWidth(location), withZone);
        plugin.Fonts.Body13.Pop();
        LocationButtons(location, id);
    }

    // Muted "zone x, y" plus the aetheryte hint, continuing the current line.
    // reservedRight is the width of whatever the line ends with (buttons, a
    // price), so the hint can drop out instead of running under it.
    private void LocationTail(MapLocation location, float reservedRight, bool withZone = true)
    {
        var coords = Coords(location.MapX, location.MapY);
        InlineText(withZone ? $"{location.ZoneName} {coords}" : coords, Theme.Neutral600);
        DrawAetheryteTail(location, reservedRight);
    }

    // [Teleport] [Flag] at the right edge of the line just drawn.
    private void LocationButtons(MapLocation location, string id, bool compact = false, float reserved = 0f)
    {
        PushRowButtonStyle();
        DrawLocationActions(location, id, compact, reserved);
        PopRowButtonStyle();
    }

    // Teleport needs the setting on, the Teleporter plugin loaded, and the
    // aetheryte attuned (TeleportService); otherwise only the flag shows.
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

        ColorText(text, Theme.Neutral500);
    }
}
