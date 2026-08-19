using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
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

        Size = new Vector2(340, 170);
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
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderInt("Max wiki results", ref maxResults, 1, 10))
        {
            config.MaxWikiResults = maxResults;
            config.Save();
        }

        var contextMenu = config.ContextMenuEnabled;
        if (ImGui.Checkbox("Add \"Look up on Wikiway\" to right-click menus", ref contextMenu))
        {
            config.ContextMenuEnabled = contextMenu;
            config.Save();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Shows on inventory items, chat item links, and NPCs.");

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
            "Adds an Unlock Requirements section with the prerequisite quest chain; " +
            "each entry jumps to that quest in the Quests tab.");

        ImGui.Separator();
        if (ImGui.Button("Clear wiki cache"))
            _ = Task.Run(() => plugin.CacheStore.ClearAsync(CancellationToken.None));

        ImGui.SameLine();
        if (ImGui.Button("Show tutorial"))
            plugin.ShowTutorial();
    }
}
