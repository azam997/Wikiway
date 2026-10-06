using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Wikiway.Core.Models;
using Wikiway.Core.Pipeline;
using Wikiway.Plugin.GameIntegration;
using Wikiway.Plugin.Ui;

namespace Wikiway.Plugin.Windows;

// The search window. This file holds its lifecycle, the search box and Back
// history, the active-quest picker, and starting searches and picking up their
// results (RunQuery, Harvest). Results are drawn by the other MainWindow.*.cs
// partials: Results (the list), Rows (collapsed rows), *Detail (expanded
// cards), RowParts and Locations (shared pieces).
public partial class MainWindow : Window, IDisposable
{
    // Results scoring below this go under the LOW CONFIDENCE strip.
    private const double ScoreGate = 0.2;
    private const int StyleColorCount = 21;
    private const int StyleVarCount = 3;
    private const int QueryCapacity = 256;
    private const int MaxHistory = 20;

    // The example names in the hint are covered by canary tests, so they always
    // return a result.
    private const string InputHint = "where is momodi, item: iron ingot...";

    private readonly Plugin plugin;
    private readonly SearchSession session = new();
    // Earlier searches for the Back button, oldest first.
    private readonly List<string> history = [];

    private bool focusInput;
    // A search requested mid-draw (a link or View button). Starting it right
    // away would replace the result list while it is being iterated, so it runs
    // at the top of the next frame.
    private (string Query, SearchCategory Category)? queuedNavigation;
    private List<ActiveQuestEntry> activeQuests = [];
    private long activeQuestsRefreshAt;
    private readonly HashSet<string> revealedSpoilers = [];

    public MainWindow(Plugin plugin) : base("Wikiway")
    {
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 240),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };

        // A spoiler reveal only counts for the current character; the next
        // login may be an alt whose progress differs.
        Plugin.ClientState.Logout += OnLogout;
    }

    public void Dispose()
    {
        Plugin.ClientState.Logout -= OnLogout;
        session.Dispose();
    }

    internal Task? PendingSearch => session.Pending;

    private void OnLogout(int type, int code) => revealedSpoilers.Clear();

    // Callers run on the game main thread outside Draw (commands, context
    // menu, notification clicks); mid-draw paths queue via queuedNavigation.
    public void SubmitQuery(string query)
    {
        if (query.Length > QueryCapacity)
            query = query[..QueryCapacity];
        session.QueryInput = query;
        RunQuery();
    }

    public override void OnOpen() => focusInput = true;

    // The window's color theme, pushed around every Draw (popped in PostDraw).
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
        var root = ImGuiUnwind.Mark();
        try
        {
            plugin.QuestProgress.RefreshIfStale();
            plugin.Teleport.RefreshIfStale();

            if (queuedNavigation is { } nav)
            {
                queuedNavigation = null;
                SubmitQuery(QueryNormalizer.Prefixed(nav.Category, nav.Query));
            }

            if (session.ScrollToTop)
            {
                ImGui.SetScrollY(0);
                session.ScrollToTop = false;
            }

            DrawBrandStrip();
            DrawSearchInput();
            Widgets.FadingRule();
            ImGui.Spacing();

            Harvest();

            // Once a search is showing, the journal list moves behind a button
            // above the results instead.
            if (QuestPickerVisible && (session.Pending != null || session.Response != null || session.Error != null))
                DrawQuestPicker();

            if (session.Pending != null)
                DrawPendingState();
            else if (session.Error != null)
                DrawErrorState();
            else if (session.Response == null)
                DrawIdleState();
            else
                DrawResults(session.Response);
        }
        catch (Exception e)
        {
            // Dalamud would catch this too, but it swaps the window for an
            // error panel until the user retries; recovering here keeps it
            // alive, and dropping the response removes whatever crashed it.
            // Neither catch pops what was pushed before the throw, hence the
            // explicit unwind - see ImGuiUnwind.
            Plugin.Log.Error(e, "Main window draw failed; dropping the current result view.");
            session.Response = null;
            session.Error ??= "something went wrong drawing results - run the search again";
            ImGuiUnwind.To(root);
        }
    }

    private static readonly string LogoPath = Path.Combine(
        Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "images", "logo64.png");

    private void DrawBrandStrip()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        fonts.Brand18.Push();
        if (Plugin.TextureProvider.GetFromFile(LogoPath).GetWrapOrDefault() is { } logo)
        {
            var side = ImGui.GetTextLineHeight();
            ImGui.Image(logo.Handle, new Vector2(side, side));
            ImGui.SameLine(0, Theme.Space2 * scale);
        }

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
        ColorText("LOCAL GAME DATA + CONSOLEGAMESWIKI", Theme.Neutral500);
        fonts.Small11.Pop();
        ImGui.Spacing();
    }

    // Back button, help marker, the query box with its search glyph, and the
    // Search button.
    private void DrawSearchInput()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;
        var dl = ImGui.GetWindowDrawList();

        fonts.Body14.Push();
        var searchWidth = ImGui.CalcTextSize("Search").X + (Theme.Space4 * 2f * scale);
        fonts.Body14.Pop();

        DrawBackButton();

        ImGui.SameLine(0, Theme.Space2 * scale);
        ImGuiComponents.HelpMarker(
            "Plain names or questions both work. Ex: \"where is momodi\" or just \"momodi\". " +
            "Start with item:, quest:, npc: or gather: to search only that kind. " +
            "Ex: \"quest: the ultimate weapon\".");

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
        var submitted = ImGui.InputTextWithHint("##wikiway-query", InputHint,
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

        if (submitted)
            RunQuery();
    }

    private void DrawBackButton()
    {
        var scale = ImGuiHelpers.GlobalScale;

        plugin.Fonts.Body14.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space3, Theme.Space2) * scale);
        ImGui.BeginDisabled(history.Count == 0);
        var clicked = Widgets.GhostButton($"{FontAwesomeIcon.ArrowLeft.ToIconString()}##wikiway-back");
        ImGui.EndDisabled();
        ImGui.PopStyleVar();
        plugin.Fonts.Body14.Pop();

        if (history.Count > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip($"Back to \"{history[^1]}\"");

        if (!clicked)
            return;

        session.QueryInput = history[^1];
        history.RemoveAt(history.Count - 1);
        RunQuery(remember: false);
    }

    private bool QuestPickerVisible => plugin.Configuration.ActiveQuestPickerEnabled;

    // Once a search is on screen, the journal list moves into this popup
    // button.
    private void DrawQuestPicker()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var fonts = plugin.Fonts;

        fonts.Citation12.Push();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(Theme.Space4, Theme.Space2) * scale);
        var label = $"{FontAwesomeIcon.Scroll.ToIconString()} Active quests";
        if (Widgets.OutlinedButton($"{label}##wikiway-questpick-btn"))
        {
            activeQuests = ActiveQuestReader.Read();
            ImGui.OpenPopup("##wikiway-questpick");
        }

        ImGui.PopStyleVar();
        fonts.Citation12.Pop();
        ImGui.Spacing();

        if (!ImGui.BeginPopup("##wikiway-questpick"))
            return;

        fonts.Body13.Push();
        if (activeQuests.Count == 0)
            ColorText("No active quests.", Theme.Neutral600);

        for (var i = 0; i < activeQuests.Count; i++)
        {
            if (QuestSelectable(activeQuests[i], $"##questpick{i}"))
                ImGui.CloseCurrentPopup();
        }

        fonts.Body13.Pop();
        ImGui.EndPopup();
    }

    // One journal quest; tracked quests are starred and listed first.
    private bool QuestSelectable(ActiveQuestEntry quest, string id)
    {
        var label = quest.Level > 0 ? $"{quest.Name}   Lv {quest.Level}" : quest.Name;
        if (quest.Tracked)
            label = $"{FontAwesomeIcon.Star.ToIconString()} {label}";
        if (quest.Tracked)
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.Accent100);
        var clicked = ImGui.Selectable(label + id);
        if (quest.Tracked)
            ImGui.PopStyleColor();

        if (clicked)
            queuedNavigation = (quest.Name, SearchCategory.Unlocks);
        return clicked;
    }

    private void DrawPendingState()
    {
        Widgets.Spinner();
        ImGui.SameLine(0, Theme.Space3 * ImGuiHelpers.GlobalScale);
        TextLine("Searching…", plugin.Fonts.Body14, Theme.Neutral400);
    }

    private void DrawErrorState() => TextLine(session.Error!, plugin.Fonts.Body14, Theme.Accent300, wrap: true);

    private void DrawIdleState()
    {
        if (QuestPickerVisible && RefreshedActiveQuests() is { Count: > 0 } quests)
        {
            DrawActiveQuestList(quests);
            return;
        }

        TextLine("Type a question or a name and hit enter.", plugin.Fonts.Body14, Theme.Neutral400);
    }

    // The journal changes rarely, so the idle list re-reads it on a slow tick
    // instead of every frame.
    private List<ActiveQuestEntry> RefreshedActiveQuests()
    {
        if (Environment.TickCount64 >= activeQuestsRefreshAt)
        {
            activeQuests = ActiveQuestReader.Read();
            activeQuestsRefreshAt = Environment.TickCount64 + 2000;
        }

        return activeQuests;
    }

    private void DrawActiveQuestList(List<ActiveQuestEntry> quests)
    {
        var fonts = plugin.Fonts;

        DetailLabel("ACTIVE QUESTS");
        ImGui.Spacing();

        fonts.Body13.Push();
        for (var i = 0; i < quests.Count; i++)
            QuestSelectable(quests[i], $"##idlequest{i}");
        fonts.Body13.Pop();

        ImGui.Spacing();
        TextLine("Click a quest to look it up.", fonts.Citation12, Theme.Neutral600);
    }

    // The pipeline runs on the thread pool; the draw loop just polls the task.
    private void Harvest()
    {
        if (session.Pending is not { IsCompleted: true })
            return;

        if (session.Pending.IsCompletedSuccessfully)
        {
            session.Response = session.Pending.Result;
            session.AboveGate.Clear();
            session.BelowGate.Clear();
            session.Wiki.Clear();
            // Wiki rows skip the score gate: their scores rank page quality,
            // not relevance, and putting them under "low confidence" would hide
            // the link to the wiki page.
            foreach (var hit in session.Response.Results)
            {
                if (hit is WikiPageResult or WikiSectionsResult)
                    session.Wiki.Add(hit);
                else
                    (hit.Score < ScoreGate ? session.BelowGate : session.AboveGate).Add(hit);
            }

            session.HasGameResult = session.AboveGate.Exists(hit => hit is EntityCardResult);
            session.LowRelevanceOpen = false;
            session.MoreOpen = false;
            session.WikiOpen = true;
            session.ExpandedRows.Clear();
            session.ExpandedChains.Clear();
            session.ExpandedScenes.Clear();
            // Grouping cutscene appearances here keeps the per-frame scene
            // tab from re-running GroupBy on every draw.
            session.SceneGroups.Clear();
            foreach (var hit in session.Response.Results)
            {
                if (hit is EntityCardResult { CutsceneAppearances.Count: > 0 } sceneCard)
                    session.SceneGroups[RowKey(sceneCard)] = sceneCard.CutsceneAppearances
                        .GroupBy(a => (a.ExpansionOrder, a.Expansion))
                        .Select(g => new SceneGroup(g.Key.ExpansionOrder, g.Key.Expansion, g.ToList()))
                        .ToList();
            }

            if (session.AboveGate.Count > 0 && session.AboveGate[0] is EntityCardResult top && HasDetail(top))
                session.ExpandedRows.Add(RowKey(top));
        }
        else if (!session.Pending.IsCanceled)
        {
            var failure = session.Pending.Exception?.GetBaseException();
            Plugin.Log.Error(failure, "Search failed.");
            session.Error = failure?.Message ?? "something went wrong";
        }

        session.Pending = null;
    }

    // Starts a search on the thread pool, cancelling any search still running.
    // remember is false when Back re-runs an older query, so history isn't re-
    // added.
    private void RunQuery(bool remember = true)
    {
        var query = session.QueryInput.Trim();
        if (query.Length == 0)
            return;

        if (remember && session.Submitted is { } previous && previous != query)
        {
            history.Add(previous);
            if (history.Count > MaxHistory)
                history.RemoveAt(0);
        }

        session.Submitted = query;
        session.Cts?.Cancel();
        session.Cts?.Dispose();
        session.Cts = new CancellationTokenSource();
        var ct = session.Cts.Token;

        session.Response = null;
        session.Error = null;
        session.ScrollToTop = true;
        // The pipeline runs off-thread, so the progress snapshot it filters
        // against must be captured here, still on the main thread.
        plugin.QuestProgress.Refresh();
        session.Pending = Task.Run(() => plugin.Pipeline.ExecuteAsync(query, ct), ct);
    }
}
