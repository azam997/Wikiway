using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Wikiway.Plugin.Windows;

// The settings window: one checkbox per Configuration toggle, each saved the
// moment it changes, plus cache and tutorial buttons.
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
        Setting("Search the wiki when local data has no answer",
            config.WikiSearchEnabled, static (c, v) => c.WikiSearchEnabled = v);

        var maxResults = config.MaxWikiResults;
        ImGui.SetNextItemWidth(120 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Max wiki results", ref maxResults, 1, 10))
            config.MaxWikiResults = maxResults;
        // SliderInt reports a change on every dragged frame; write the file
        // once on release instead.
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();

        Setting("Add \"Look up on Wikiway\" to right-click menus",
            config.ContextMenuEnabled, static (c, v) => c.ContextMenuEnabled = v,
            "Appears when right-clicking items, NPCs and enemies. Items work from your inventory, " +
            "chat links, the crafting and gathering logs, the timers window and custom deliveries.");

        Setting("Offer a duty guide when entering a solo duty",
            config.SoloDutyToastEnabled, static (c, v) => c.SoloDutyToastEnabled = v,
            "A small notification appears bottom-right; click it to open the guide.");

        Setting("Show unlock requirements on quest and duty cards",
            config.ShowUnlockRequirements, static (c, v) => c.ShowUnlockRequirements = v,
            "Adds an Unlock Requirements section listing each quest chain. " +
            "Clicking a step looks that quest up, and completed " +
            "steps get checkmarks while you are logged in.");

        Setting("Show equipment stats on item cards",
            config.ShowEquipmentStats, static (c, v) => c.ShowEquipmentStats = v,
            "Equippable items get an Equipment section and food or medicine a Consumable " +
            "section on the expanded card. Ex: item level, stats, buff duration - read from the game files.");

        Setting("Show usage on item cards",
            config.ShowItemUsage, static (c, v) => c.ShowItemUsage = v,
            "Items used somewhere get a Usage section on their expanded card. " +
            "Ex: recipes, custom deliveries, collectable turn-ins, treasure map sites, materia stats.");

        Setting("Show cutscene appearances on NPC cards",
            config.ShowCutsceneAppearances, static (c, v) => c.ShowCutsceneAppearances = v,
            "Adds a Cutscene Appearances tab on expanded NPC cards, grouped by expansion, " +
            "hidden if the user has not progressed past that point in MSQ.");

        Setting("Cap location lists on NPC results",
            config.CapNpcLocationPins, static (c, v) => c.CapNpcLocationPins = v,
            "NPC results list at most 6 map pins; a summary line counts the rest. " +
            "Turn off to always list every location.");

        Setting("Show the nearest aetheryte on map locations",
            config.ShowNearestAetheryte, static (c, v) => c.ShowNearestAetheryte = v,
            "Each location line names the closest aetheryte on that map, or the closest " +
            "aethernet shard inside cities. Ex: Western Thanalan 27.5, 17.0 · Aetheryte: Horizon");

        Setting("Add a Teleport button next to Flag map",
            config.TeleportButtonEnabled, static (c, v) => c.TeleportButtonEnabled = v,
            "Needs the Teleporter plugin. The button only appears for " +
            "aetherytes you have attuned to, and teleports there when clicked.");

        Setting("Hide spoilers past your MSQ progress",
            config.SpoilerProtectionEnabled, static (c, v) => c.SpoilerProtectionEnabled = v,
            "Uses your character's quest progress. Cutscenes you haven't reached are " +
            "hidden, and duty and Main Scenario names past your progress are blurred " +
            "until clicked. Does nothing while logged out.");

        Setting("Show your active quests before you search",
            config.ActiveQuestPickerEnabled, static (c, v) => c.ActiveQuestPickerEnabled = v,
            "Lists the quests in your journal before you search, and behind " +
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

    // A checkbox that saves the config as soon as it's toggled, followed by a
    // "(?)" help marker when there's help text.
    private void Setting(string label, bool value, Action<Configuration, bool> set, string help = "")
    {
        if (ImGui.Checkbox(label, ref value))
        {
            set(config, value);
            config.Save();
        }

        if (help.Length == 0)
            return;

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(help);
    }
}
