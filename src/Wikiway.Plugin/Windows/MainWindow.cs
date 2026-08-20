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
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

public partial class MainWindow : Window, IDisposable
{
    private const double ScoreGate = 0.2;
    private const int StyleColorCount = 21;
    private const int StyleVarCount = 3;

    // Hint entities are canary-pinned names, so the samples always resolve.
    private static readonly (string Label, string Hint, SearchCategory Value)[] Categories =
    [
        ("Items", "how do i get an iron ingot...", SearchCategory.Items),
        ("Quests", "the ultimate weapon...", SearchCategory.Quests),
        ("Gathering", "where can i get iron ore...", SearchCategory.Gathering),
        ("Unlockables", "how do i unlock the gold saucer...", SearchCategory.Unlockables),
        ("NPCs", "where is momodi...", SearchCategory.Npcs),
        ("Other", "what is the aurum vale...", SearchCategory.Other),
    ];

    private readonly Plugin plugin;
    private readonly SearchSession[] sessions;

    private int categoryIndex = Categories.Length - 1;
    private bool focusInput;
    private (string Query, SearchCategory Category)? queuedNavigation;
    private List<ActiveQuestEntry> activeQuests = [];
    private readonly HashSet<string> revealedSpoilers = [];

    private SearchSession Active => sessions[categoryIndex];

    public MainWindow(Plugin plugin) : base("Wikiway")
    {
        this.plugin = plugin;

        sessions = new SearchSession[Categories.Length];
        for (var i = 0; i < sessions.Length; i++)
            sessions[i] = new SearchSession();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        // Spoiler reveals are consent from this character; the next login may
        // be an alt whose progress differs.
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.ClientState.Logout -= OnLogout;
        foreach (var session in sessions)
            session.Dispose();
    }

    private void OnLogout(int type, int code) => revealedSpoilers.Clear();

    public void SubmitQuery(string query) => SubmitQuery(query, SearchCategory.Other);

    public void SubmitQuery(string query, SearchCategory category)
    {
        categoryIndex = Array.FindIndex(Categories, c => c.Value == category);
        Active.QueryInput = query;
        RunQuery(Active, category);
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
        plugin.QuestProgress.RefreshIfStale();

        if (queuedNavigation is { } nav)
        {
            queuedNavigation = null;
            SubmitQuery(nav.Query, nav.Category);
        }

        if (Active.PendingScroll == null)
            Active.ScrollY = ImGui.GetScrollY();

        DrawBrandStrip();
        DrawCategoryStrip();

        ImGui.PushID(categoryIndex);
        if (Active.PendingScroll is { } scrollY)
        {
            ImGui.SetScrollY(scrollY);
            Active.PendingScroll = null;
        }

        DrawSearchInput();
        Widgets.FadingRule();
        ImGui.Spacing();

        HarvestAll();

        if (Active.Pending != null)
            DrawPendingState();
        else if (Active.Error != null)
            DrawErrorState();
        else if (Active.Response == null)
            DrawIdleState();
        else
            DrawResults(Active.Response);
        ImGui.PopID();
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

    private void DrawCategoryStrip()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();

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
            if (ImGui.Button($"{Categories[i].Label}##seg{i}") && i != categoryIndex)
            {
                categoryIndex = i;
                Active.PendingScroll = Active.ScrollY;
            }

            ImGui.PopStyleColor(4);

            if (sessions[i].Pending != null)
            {
                var max = ImGui.GetItemRectMax();
                var min = ImGui.GetItemRectMin();
                dl.AddCircleFilled(new Vector2(max.X - (4f * scale), min.Y + (4f * scale)), 2f * scale, Theme.AccentU);
            }
        }

        ImGui.EndGroup();
        ImGui.PopStyleVar(3);
        fonts.Body13.Pop();
        dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Theme.DividerU, Theme.RadiusMd * scale);

        ImGui.SameLine(0, Theme.Space3 * scale);
        ImGuiComponents.HelpMarker(
            "Narrows the search: Items focuses on acquisition, Quests on unlocks, " +
            "Duties on guides, Unlockables on optional content and how to unlock it, " +
            "NPCs on locations. Other searches everything. Each tab keeps its own search.");
    }

    private void DrawSearchInput()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();
        var session = Active;

        fonts.Body14.Push();
        var searchWidth = ImGui.CalcTextSize("Search").X + (Theme.Space4 * 2f * scale);
        var questWidth = plugin.Configuration.ActiveQuestPickerEnabled
            ? ImGui.CalcTextSize(FontAwesomeIcon.Scroll.ToIconString()).X + (Theme.Space4 * 2f * scale) + (Theme.Space2 * scale)
            : 0f;
        fonts.Body14.Pop();
        var helpWidth = ImGui.CalcTextSize("(?)").X + (Theme.Space2 * scale);

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
        ImGui.SetNextItemWidth(-(searchWidth + questWidth + helpWidth + (Theme.Space3 * scale)));
        var submitted = ImGui.InputTextWithHint("##wikiway-query", Categories[categoryIndex].Hint,
            ref session.QueryInput, 256, ImGuiInputTextFlags.EnterReturnsTrue);
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

        if (plugin.Configuration.ActiveQuestPickerEnabled)
            DrawQuestPicker(scale);

        ImGui.SameLine(0, Theme.Space2 * scale);
        ImGuiComponents.HelpMarker(
            "Plain names or questions both work — \"where is momodi\" strips the phrasing, " +
            "and a leading the/a/an is ignored. From chat, /wikiway accepts scoped searches: " +
            "item:, quest:, gather:, npc:, unlock:.");

        if (submitted)
            RunQuery(session, Categories[categoryIndex].Value);
    }

    private void DrawQuestPicker(float scale)
    {
        var fonts = plugin.Fonts;

        fonts.Body14.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
        ImGui.SameLine(0, Theme.Space2 * scale);
        if (Widgets.OutlinedButton($"{FontAwesomeIcon.Scroll.ToIconString()}##wikiway-questpick-btn"))
        {
            activeQuests = ActiveQuestReader.Read();
            ImGui.OpenPopup("##wikiway-questpick");
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Active quests");
        ImGui.PopStyleVar();
        fonts.Body14.Pop();

        if (!ImGui.BeginPopup("##wikiway-questpick"))
            return;

        fonts.Body13.Push();
        if (activeQuests.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Neutral600);
            ImGui.TextUnformatted("No active quests.");
            ImGui.PopStyleColor();
        }

        for (var i = 0; i < activeQuests.Count; i++)
        {
            var quest = activeQuests[i];
            var label = quest.Level > 0 ? $"{quest.Name}   Lv {quest.Level}" : quest.Name;
            if (quest.Tracked)
                ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent100);
            if (ImGui.Selectable($"{label}##questpick{i}"))
            {
                queuedNavigation = (quest.Name, SearchCategory.Quests);
                ImGui.CloseCurrentPopup();
            }

            if (quest.Tracked)
                ImGui.PopStyleColor();
        }

        fonts.Body13.Pop();
        ImGui.EndPopup();
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
        ImGui.TextWrapped(Active.Error!);
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

    // The pipeline runs on the thread pool; the draw loop just polls the
    // tasks. Background tabs harvest too, so their searches finish unwatched.
    private void HarvestAll()
    {
        foreach (var session in sessions)
        {
            if (session.Pending is not { IsCompleted: true })
                continue;

            if (session.Pending.IsCompletedSuccessfully)
            {
                session.Response = session.Pending.Result;
                session.AboveGate.Clear();
                session.BelowGate.Clear();
                foreach (var hit in session.Response.Results)
                    (hit.Score < ScoreGate ? session.BelowGate : session.AboveGate).Add(hit);
                session.LowRelevanceOpen = false;
                session.ExpandedRows.Clear();
                session.ExpandedChains.Clear();
                if (session.AboveGate.Count > 0 && session.AboveGate[0] is EntityCardResult top && HasDetail(top))
                    session.ExpandedRows.Add(RowKey(top));
            }
            else if (!session.Pending.IsCanceled)
            {
                session.Error = session.Pending.Exception?.GetBaseException().Message ?? "something went wrong";
            }

            session.Pending = null;
        }
    }

    private void RunQuery(SearchSession session, SearchCategory category)
    {
        var query = session.QueryInput.Trim();
        if (query.Length == 0)
            return;

        session.Cts?.Cancel();
        session.Cts?.Dispose();
        session.Cts = new CancellationTokenSource();
        var ct = session.Cts.Token;

        session.Response = null;
        session.Error = null;
        session.PendingScroll = 0f;
        // The pipeline runs off-thread, so the progress snapshot it filters
        // against must be captured here, still on the main thread.
        plugin.QuestProgress.Refresh();
        session.Pending = Task.Run(() => plugin.Pipeline.ExecuteAsync(query, category, ct), ct);
    }
}
