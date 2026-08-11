using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Wikiway.Plugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private string queryInput = string.Empty;
    private string? lastQuery;
    private bool focusInput;

    public MainWindow(Plugin plugin) : base("Wikiway")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 220),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose() { }

    public void SubmitQuery(string query)
    {
        queryInput = query;
        RunQuery();
    }

    public override void OnOpen() => focusInput = true;

    public override void Draw()
    {
        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }

        ImGui.SetNextItemWidth(-70);
        var submitted = ImGui.InputTextWithHint("##wikiway-query", "where is momodi...", ref queryInput, 256,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        submitted |= ImGui.Button("Search");

        if (submitted && queryInput.Trim().Length > 0)
            RunQuery();

        ImGui.Separator();

        if (lastQuery == null)
        {
            ImGui.TextDisabled("Type a question or a name and hit enter.");
            return;
        }

        // Placeholder until the query pipeline is in - just echo for now.
        ImGui.TextWrapped($"You asked: {lastQuery}");
    }

    private void RunQuery()
    {
        lastQuery = queryInput.Trim();
        Plugin.Log.Debug("Query submitted: {Query}", lastQuery);
    }
}
