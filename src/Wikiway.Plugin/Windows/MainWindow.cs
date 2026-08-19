using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Wikiway.Core.Models;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

public partial class MainWindow : Window, IDisposable
{
    private const double ScoreGate = 0.2;
    private const int StyleColorCount = 21;
    private const int StyleVarCount = 3;

    private static readonly (string Label, SearchCategory Value)[] Categories =
    [
        ("Items", SearchCategory.Items),
        ("Quests", SearchCategory.Quests),
        ("Duties", SearchCategory.Duties),
        ("NPCs", SearchCategory.Npcs),
        ("Other", SearchCategory.Other),
    ];

    private readonly Plugin plugin;
    private readonly List<SearchResult> aboveGate = [];
    private readonly List<SearchResult> belowGate = [];
    private readonly HashSet<string> expandedRows = [];

    private string queryInput = string.Empty;
    private int categoryIndex = Categories.Length - 1;
    private bool focusInput;
    private bool lowRelevanceOpen;

    private CancellationTokenSource? queryCts;
    private Task<QueryResponse>? pending;
    private QueryResponse? response;
    private string? error;

    public MainWindow(Plugin plugin) : base("Wikiway")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
        queryCts?.Cancel();
        queryCts?.Dispose();
    }

    public void SubmitQuery(string query) => SubmitQuery(query, SearchCategory.Other);

    public void SubmitQuery(string query, SearchCategory category)
    {
        queryInput = query;
        categoryIndex = Array.FindIndex(Categories, c => c.Value == category);
        RunQuery();
    }

    public override void OnOpen() => focusInput = true;

    public override void PreDraw()
    {
        base.PreDraw();
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Theme.Bg);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Text);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, Theme.Divider);
        ImGui.PushStyleColor(ImGuiCol.Separator, Theme.Divider);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Accent900);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent800);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Theme.Bg);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Theme.Neutral800);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, Theme.Neutral700);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, Theme.Accent700);
        ImGui.PushStyleColor(ImGuiCol.Header, Theme.Surface);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Theme.Accent900);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Theme.Accent800);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, Theme.RadiusMd * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, Theme.RadiusMd * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Theme.Space4, Theme.Space3) * scale);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(StyleVarCount);
        ImGui.PopStyleColor(StyleColorCount);
        base.PostDraw();
    }

    public override void Draw()
    {
        DrawBrandStrip();
        DrawSearchRow();
        Widgets.FadingRule();
        ImGui.Spacing();

        HarvestPending();

        if (pending != null)
        {
            DrawPendingState();
            return;
        }

        if (error != null)
        {
            DrawErrorState();
            return;
        }

        if (response == null)
        {
            DrawIdleState();
            return;
        }

        DrawResults(response);
    }

    private void DrawBrandStrip()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        fonts.Brand18.Push();
        ImGui.TextUnformatted("Wikiway");
        fonts.Brand18.Pop();
        var brandMin = ImGui.GetItemRectMin();
        var brandMax = ImGui.GetItemRectMax();

        ImGui.SameLine(0, Theme.Space3 * scale);
        var tick = ImGui.GetCursorScreenPos();
        var tickY = (brandMin.Y + brandMax.Y) * 0.5f;
        ImGui.GetWindowDrawList().AddRectFilled(
            new Vector2(tick.X, tickY),
            new Vector2(tick.X + (18f * scale), tickY + MathF.Max(1f, scale)),
            Theme.AccentU);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (18f * scale) + (Theme.Space3 * scale));

        fonts.Small11.Push();
        var pos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(pos with { Y = brandMax.Y - ImGui.GetTextLineHeight() - (3f * scale) });
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral500);
        ImGui.TextUnformatted("LOCAL GAME DATA + CONSOLEGAMESWIKI");
        ImGui.PopStyleColor();
        fonts.Small11.Pop();
        ImGui.Spacing();
    }

    private void DrawSearchRow()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();

        fonts.Body14.Push();
        var searchWidth = ImGui.CalcTextSize("Search").X + (Theme.Space4 * 2f * scale);
        fonts.Body14.Pop();

        fonts.Body13.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
        ImGui.BeginGroup();
        for (var i = 0; i < Categories.Length; i++)
        {
            if (i > 0)
            {
                ImGui.SameLine();
                var edge = ImGui.GetCursorScreenPos();
                dl.AddLine(edge, edge with { Y = ImGui.GetItemRectMax().Y }, Theme.DividerU);
            }

            var active = i == categoryIndex;
            ImGui.PushStyleColor(ImGuiCol.Button, active ? Theme.Accent800 : Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, active ? Theme.Accent800 : Theme.Accent900);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Accent800);
            ImGui.PushStyleColor(ImGuiCol.Text, active ? Theme.Accent100 : Theme.Neutral500);
            if (ImGui.Button($"{Categories[i].Label}##seg{i}"))
                categoryIndex = i;
            ImGui.PopStyleColor(4);
        }

        ImGui.EndGroup();
        ImGui.PopStyleVar(3);
        fonts.Body13.Pop();
        dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Theme.DividerU, Theme.RadiusMd * scale);

        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGuiComponents.HelpMarker(
            "Narrows the search: Items focuses on acquisition, Quests on unlocks, " +
            "Duties on guides, NPCs on locations. Other searches everything.");

        ImGui.SameLine(0, Theme.Space3 * scale);
        if (focusInput)
        {
            ImGui.SetKeyboardFocusHere();
            focusInput = false;
        }

        var glyph = FontAwesomeIcon.Search.ToIconString();
        fonts.Citation12.Push();
        var glyphSize = ImGui.CalcTextSize(glyph);
        fonts.Citation12.Pop();

        fonts.Body14.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
            new Vector2(glyphSize.X + (Theme.Space3 * 2f * scale), Theme.Space2 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        var inputPos = ImGui.GetCursorScreenPos();
        ImGui.SetNextItemWidth(-(searchWidth + (Theme.Space3 * scale)));
        var submitted = ImGui.InputTextWithHint("##wikiway-query", "where is momodi...", ref queryInput, 256,
            ImGuiInputTextFlags.EnterReturnsTrue);
        var inputHeight = ImGui.GetItemRectSize().Y;
        ImGui.PopStyleVar(2);
        fonts.Body14.Pop();

        fonts.Citation12.Push();
        dl.AddText(
            inputPos + new Vector2(Theme.Space3 * scale, (inputHeight - glyphSize.Y) * 0.5f),
            Theme.Neutral600U,
            glyph);
        fonts.Citation12.Pop();

        ImGui.SameLine(0, Theme.Space3 * scale);
        fonts.Body14.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
        submitted |= Widgets.OutlinedButton("Search");
        ImGui.PopStyleVar();
        fonts.Body14.Pop();

        if (submitted)
            RunQuery();
    }

    private void DrawPendingState()
    {
        Widgets.Spinner();
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        plugin.Fonts.Body14.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        ImGui.TextUnformatted("Searching…");
        ImGui.PopStyleColor();
        plugin.Fonts.Body14.Pop();
    }

    private void DrawErrorState()
    {
        plugin.Fonts.Body14.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent300);
        ImGui.TextWrapped(error!);
        ImGui.PopStyleColor();
        plugin.Fonts.Body14.Pop();
    }

    private void DrawIdleState()
    {
        plugin.Fonts.Body14.Push();
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral400);
        ImGui.TextUnformatted("Type a question or a name and hit enter.");
        ImGui.PopStyleColor();
        plugin.Fonts.Body14.Pop();
    }

    // The pipeline runs on the thread pool; the draw loop just polls the task.
    private void HarvestPending()
    {
        if (pending is not { IsCompleted: true })
            return;

        if (pending.IsCompletedSuccessfully)
        {
            response = pending.Result;
            aboveGate.Clear();
            belowGate.Clear();
            foreach (var hit in response.Results)
                (hit.Score < ScoreGate ? belowGate : aboveGate).Add(hit);
            lowRelevanceOpen = false;
            expandedRows.Clear();
            if (aboveGate.Count > 0 && aboveGate[0] is EntityCardResult top && HasDetail(top))
                expandedRows.Add(RowKey(top));
        }
        else if (!pending.IsCanceled)
        {
            error = pending.Exception?.GetBaseException().Message ?? "something went wrong";
        }

        pending = null;
    }

    private void RunQuery()
    {
        var query = queryInput.Trim();
        if (query.Length == 0)
            return;

        queryCts?.Cancel();
        queryCts = new CancellationTokenSource();
        var ct = queryCts.Token;

        response = null;
        error = null;
        var category = Categories[categoryIndex].Value;
        pending = Task.Run(() => plugin.Pipeline.ExecuteAsync(query, category, ct), ct);
    }
}
