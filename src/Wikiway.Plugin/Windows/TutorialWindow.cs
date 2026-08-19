using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Wikiway.Plugin.Windows;

public class TutorialWindow : Window
{
    private static readonly (string Title, string Body)[] Pages =
    [
        ("Welcome to Wikiway",
            "Wikiway looks things up for you: local game data first, the wiki second.\n\n" +
            "Open it any time with /wikiway (or /wway), optionally with a question - " +
            "\"/wikiway where is momodi\".\n\n" +
            "You can scope a command search with a prefix: \"/wikiway quest:the ultimate weapon\" " +
            "(item:, quest:, duty:, npc:, area:)."),
        ("Search categories",
            "The tabs above the search box narrow what a search means, and each tab " +
            "keeps its own search and results.\n\n" +
            "Items - how to obtain an item: marketboard, vendors, drops, crafting.\n" +
            "Quests - unlocks, prerequisites and a quick path to the wiki page.\n" +
            "Duties - guides pulled straight from the wiki.\n" +
            "Areas - field zones like Bozja or the Occult Crescent and how to unlock them.\n" +
            "NPCs - locations, with a map flag one click away.\n\n" +
            "Other is a free search across everything."),
        ("Right-click lookups",
            "Right-click an inventory item, an item link in chat, or an NPC and choose " +
            "\"Look up on Wikiway\" to run the matching search instantly.\n\n" +
            "This can be turned off in settings."),
        ("Flags, toasts and settings",
            "NPC results include a \"Flag map\" button that opens the map at their location.\n\n" +
            "Entering a solo duty shows a small notification - click it for a guide to that duty.\n\n" +
            "Settings live in the plugin installer or via the Wikiway window, and the (?) markers " +
            "explain individual options. Enjoy!"),
    ];

    private readonly Configuration config;
    private int page;

    public TutorialWindow(Configuration config) : base("Wikiway Tour")
    {
        this.config = config;

        Size = new Vector2(460, 260);
        SizeCondition = ImGuiCond.Appearing;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
    }

    public void Restart()
    {
        page = 0;
        IsOpen = true;
    }

    public override void OnClose() => MarkSeen();

    public override void Draw()
    {
        var (title, body) = Pages[page];

        ImGui.BeginChild("##tutorial-body", new Vector2(0, -ImGui.GetFrameHeightWithSpacing()));
        ImGui.TextUnformatted(title);
        ImGui.SameLine();
        ImGui.TextDisabled($"{page + 1} / {Pages.Length}");
        ImGui.Separator();
        ImGui.TextWrapped(body);
        ImGui.EndChild();

        if (ImGui.Button("Skip##tutorial"))
        {
            MarkSeen();
            IsOpen = false;
        }

        var lastPage = page == Pages.Length - 1;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 110);
        if (page > 0 && ImGui.Button("Back##tutorial"))
            page--;
        if (page > 0)
            ImGui.SameLine();

        if (lastPage)
        {
            if (ImGui.Button("Got it##tutorial"))
            {
                MarkSeen();
                IsOpen = false;
            }
        }
        else if (ImGui.Button("Next##tutorial"))
        {
            page++;
        }
    }

    private void MarkSeen()
    {
        if (config.TutorialSeen)
            return;

        config.TutorialSeen = true;
        config.Save();
    }
}
