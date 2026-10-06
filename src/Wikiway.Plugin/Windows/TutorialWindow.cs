using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace Wikiway.Plugin.Windows;

// First-run tour: a few pages of plain text, shown once (TutorialSeen) and
// reopenable from settings.
public class TutorialWindow : Window
{
    private static readonly (string Title, string Body)[] Pages =
    [
        ("Welcome to Wikiway",
            "Wikiway looks things up for you: local game data first, the wiki second.\n\n" +
            "Open it any time with /wikiway (or /ww), optionally with a question - " +
            "\"/wikiway where is momodi\"."),
        ("Narrowing a search",
            "Phrasing steers the results: \"where is\" favors NPCs, \"how do i get\" favors " +
            "items, \"how do i unlock\" favors quests and duties.\n\n" +
            "Start with a prefix to see only one kind of result. Ex: \"quest: the ultimate weapon\".\n" +
            "item: - how to obtain an item: marketboard, vendors, drops, crafting.\n" +
            "quest: - quests, their prerequisite chains, and optional content " +
            "from side dungeons and raids to zones like Bozja or the Firmament, " +
            "with how to unlock it all.\n" +
            "gather: - gatherable materials: node types, levels and locations.\n" +
            "npc: - locations, with a map flag one click away.\n\n" +
            "The arrow left of the search box goes back to your previous search."),
        ("Right-click lookups",
            "Right-click an item, an NPC or an enemy and choose " +
            "\"Look up on Wikiway\" to run the matching search instantly. Items work from your " +
            "inventory, chat links, the crafting and gathering logs, the timers window and " +
            "custom deliveries.\n\n" +
            "This can be turned off in settings."),
        ("Flags, toasts and settings",
            "NPC results include a \"Flag map\" button that opens the map at their location.\n\n" +
            "Entering a solo duty shows a small notification - click it to look the duty up.\n\n" +
            "Before you search, the window lists your journal's active quests - pick one to look it up.\n\n" +
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

        // Back's visibility and the trailing SameLine key off the page as it
        // was at the top of the frame, so a click doesn't shift the row until
        // the next frame; the last-page check reads the post-click page.
        var showBack = page > 0;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - (110 * ImGuiHelpers.GlobalScale));
        if (showBack && ImGui.Button("Back##tutorial"))
            page--;
        if (showBack)
            ImGui.SameLine();

        if (page == Pages.Length - 1)
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
