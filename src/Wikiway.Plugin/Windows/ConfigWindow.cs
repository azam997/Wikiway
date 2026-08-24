using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Wikiway.Plugin.Windows;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration config;

    public ConfigWindow(Plugin plugin) : base("Wikiway Settings")
    {
        this.plugin = plugin;
        config = plugin.Configuration;

        Size = new Vector2(340, 200);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var wikiEnabled = config.WikiSearchEnabled;
        if (ImGui.Checkbox("Search the wiki when local data has no answer", ref wikiEnabled))
        {
            config.WikiSearchEnabled = wikiEnabled;
            config.Save();
        }

        var maxResults = config.MaxWikiResults;
        ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Max wiki results", ref maxResults, 1, 10))
            config.MaxWikiResults = maxResults;
        // SliderInt reports a change on every dragged frame; write the file
        // once on release instead.
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();

        var contextMenu = config.ContextMenuEnabled;
        if (ImGui.Checkbox("Add \"Look up on Wikiway\" to right-click menus", ref contextMenu))
        {
            config.ContextMenuEnabled = contextMenu;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Appears when right-clicking inventory items, chat item links, and NPCs.");

        var soloToast = config.SoloDutyToastEnabled;
        if (ImGui.Checkbox("Offer a duty guide when entering a solo duty", ref soloToast))
        {
            config.SoloDutyToastEnabled = soloToast;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("A small notification appears bottom-right; click it to open the guide.");

        var unlocks = config.ShowUnlockRequirements;
        if (ImGui.Checkbox("Show unlock requirements on quest and duty cards", ref unlocks))
        {
            config.ShowUnlockRequirements = unlocks;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Adds an Unlock Requirements section listing each quest chain. " +
            "Steps link to that quest in the Quests & Unlocks tab, and completed " +
            "steps get checkmarks while you are logged in.");

        var equipStats = config.ShowEquipmentStats;
        if (ImGui.Checkbox("Show equipment stats on item cards", ref equipStats))
        {
            config.ShowEquipmentStats = equipStats;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Equippable items get an Equipment section on their expanded card. " +
            "Ex: item level, stats, materia slots, repair and resale - read from the game files.");

        var cutscenes = config.ShowCutsceneAppearances;
        if (ImGui.Checkbox("Show cutscene appearances on NPC cards", ref cutscenes))
        {
            config.ShowCutsceneAppearances = cutscenes;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Adds a Cutscene Appearances tab on expanded NPC cards, grouped by expansion, " +
            "hidden if the user has not progressed past that point in MSQ.");

        var pinCap = config.CapNpcLocationPins;
        if (ImGui.Checkbox("Cap location lists on NPC results", ref pinCap))
        {
            config.CapNpcLocationPins = pinCap;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "NPC results list at most 6 map pins; a summary line counts the rest. " +
            "Turn off to always list every location.");

        var spoilers = config.SpoilerProtectionEnabled;
        if (ImGui.Checkbox("Hide spoilers past your MSQ progress", ref spoilers))
        {
            config.SpoilerProtectionEnabled = spoilers;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Uses your character's quest progress. Cutscenes you haven't reached are " +
            "hidden, and duty and Main Scenario names past your progress are blurred " +
            "until clicked. Does nothing while logged out.");

        var questPicker = config.ActiveQuestPickerEnabled;
        if (ImGui.Checkbox("Show your active quests on the Quests & Unlocks tab", ref questPicker))
        {
            config.ActiveQuestPickerEnabled = questPicker;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(
            "Lists the quests in your journal on the tab before you search, and behind " +
            "the Active quests button after. Clicking one looks it up.");

        ImGui.Separator();
        if (ImGui.Button("Clear wiki cache"))
        {
            _ = Task.Run(async () =>
            {
                await plugin.CacheStore.ClearAsync(CancellationToken.None).ConfigureAwait(false);
                Plugin.NotificationManager.AddNotification(new Notification
                {
                    Title = "Wikiway",
                    Content = "Wiki cache cleared.",
                    Type = NotificationType.Success,
                });
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Show tutorial"))
            plugin.ShowTutorial();
    }
}
