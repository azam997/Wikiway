using Dalamud.Bindings.ImGui;

namespace Wikiway.Plugin.Ui;

// Dalamud's WindowHost catches a throwing Draw but restores nothing, and its
// ImGui build never runs ImGui's own end-of-frame recovery, so whatever was
// still pushed or begun at the throw (colours, style vars, fonts, groups, tab
// bars, popups, split draw channels) stays on the global stacks and bleeds
// into every window drawn afterwards - other plugins included - until reload.
// This mirrors ImGui::ErrorCheckEndFrameRecover, stopping at the window Draw
// was entered in so the host's own Begin/End and the PostDraw pops stay paired.
internal static class ImGuiUnwind
{
    private const int MaxNestedWindows = 32;

    public static ImGuiWindowPtr Mark() => ImGuiP.GetCurrentWindow();

    public static void To(ImGuiWindowPtr root)
    {
        // Popups and child windows opened after the mark are unwound and
        // ended innermost first. ImGui refuses End() on the frame's fallback
        // window, so the identity check is backed by a bound.
        for (var i = 0; i < MaxNestedWindows; i++)
        {
            var current = ImGuiP.GetCurrentWindow();
            if (current.IsNull || current == root)
                break;

            ImGuiP.ErrorCheckEndWindowRecover(null);
            if ((current.Flags & ImGuiWindowFlags.ChildWindow) != 0)
                ImGui.EndChild();
            else
                ImGui.End();
        }

        ImGuiP.ErrorCheckEndWindowRecover(null);
        // Recovery leaves draw-list channel splits alone; merging an unsplit
        // list is a no-op.
        ImGui.GetWindowDrawList().ChannelsMerge();
    }
}
