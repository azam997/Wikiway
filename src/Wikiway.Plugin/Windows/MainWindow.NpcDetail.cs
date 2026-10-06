using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// NPC cards: the location list, and for NPCs who also appear in quest
// cutscenes, the [Locations][Cutscene appearances] tabs of the expanded card.
public partial class MainWindow
{
    private const int MaxNpcLocationLines = 6;

    // A merged card (several same-named NPC rows, see EntityGrouper) carries
    // its deduplicated pins; a single row carries at most one.
    private static IReadOnlyList<MapLocation> NpcLocations(EntityCardResult card, NpcEntity npc) =>
        card.MergedCount > 1
            ? card.MergedLocations
            : npc.Location is { } single ? [single] : [];

    private void DrawNpcTabs(EntityCardResult card, NpcEntity npc)
    {
        var fonts = plugin.Fonts;

        ImGui.Spacing();
        fonts.Body13.Push();
        ImGui.PushStyleColor(ImGuiCol.Tab, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, Theme.Accent900);
        ImGui.PushStyleColor(ImGuiCol.TabActive, Theme.Accent800);
        if (ImGui.BeginTabBar($"##npctabs{npc.RowId}"))
        {
            if (ImGui.BeginTabItem($"Locations##npcloc{npc.RowId}"))
            {
                DrawNpcLocations(npc.RowId, NpcLocations(card, npc), card.MergedHidden);
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

    // One map-pin line per location, capped at MaxNpcLocationLines when the
    // CapNpcLocationPins setting is on (story NPCs can have dozens).
    private void DrawNpcLocations(uint npcRowId, IReadOnlyList<MapLocation> locations, int hidden)
    {
        var shown = locations.Count;
        if (plugin.Configuration.CapNpcLocationPins && locations.Count > MaxNpcLocationLines)
            shown = MaxNpcLocationLines;

        for (var i = 0; i < shown; i++)
            PinnedLocationLine(locations[i].ZoneName, locations[i], $"flag{npcRowId}-{i}");

        if (shown < locations.Count)
            TextLine($"+{locations.Count - shown} more locations", plugin.Fonts.Citation12, Theme.Neutral600);

        if (hidden > 0)
            TextLine($"{hidden} duplicate rows hidden.", plugin.Fonts.Citation12, Theme.Neutral600);
    }

    // Quests whose cutscenes place this NPC, grouped by expansion (grouping
    // is precomputed in Harvest). Each group folds behind a ghost button.
    private void DrawSceneAppearances(EntityCardResult card, NpcEntity npc)
    {
        var fonts = plugin.Fonts;
        var viewWidth = FlagButtonWidth("View");

        if (!session.SceneGroups.TryGetValue(RowKey(card), out var groups))
            return;

        foreach (var group in groups)
        {
            var key = $"{npc.RowId}:{group.Order}";
            var open = session.ExpandedScenes.Contains(key);
            var count = group.Scenes.Count;
            var summary = count == 1 ? "1 appearance" : $"{count} appearances";

            fonts.Body13.Push();
            if (Widgets.GhostButton($"{CaretGlyph(open)} {group.Expansion} — {summary}##scenes{key}") &&
                !session.ExpandedScenes.Remove(key))
                session.ExpandedScenes.Add(key);
            fonts.Body13.Pop();

            if (!open)
                continue;

            var i = 0;
            foreach (var appearance in group.Scenes)
            {
                i++;
                fonts.Body13.Push();
                ColorText(appearance.Quest.Name, Theme.Neutral300);
                if (appearance.Location is { } loc)
                    InlineText($"{loc.ZoneName} {Coords(loc.MapX, loc.MapY)}", Theme.Neutral600);
                fonts.Body13.Pop();

                QuestStepButtons(appearance.Quest.Name, appearance.Location,
                    "scene", $"{npc.RowId}-{group.Order}-{i}", viewWidth);
            }
        }
    }
}
