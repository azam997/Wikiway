using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
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

        ImGui.Separator();
        if (ImGui.Button("Clear wiki cache"))
            _ = Task.Run(() => plugin.CacheStore.ClearAsync(CancellationToken.None));
    }
}
