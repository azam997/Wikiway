using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Models;
using Wikiway.Plugin.GameIntegration;

namespace Wikiway.Plugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    private string queryInput = string.Empty;
    private bool focusInput;

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

        if (submitted)
            RunQuery();

        ImGui.Separator();

        HarvestPending();

        if (pending != null)
        {
            ImGui.TextDisabled("Searching...");
            return;
        }

        if (error != null)
        {
            ImGui.TextColored(new Vector4(0.9f, 0.4f, 0.4f, 1f), error);
            return;
        }

        if (response == null)
        {
            ImGui.TextDisabled("Type a question or a name and hit enter.");
            return;
        }

        DrawResults(response);
    }

    // The pipeline runs on the thread pool; the draw loop just polls the task.
    private void HarvestPending()
    {
        if (pending is not { IsCompleted: true })
            return;

        if (pending.IsCompletedSuccessfully)
            response = pending.Result;
        else if (!pending.IsCanceled)
            error = pending.Exception?.GetBaseException().Message ?? "something went wrong";

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
        pending = Task.Run(() => plugin.Pipeline.ExecuteAsync(query, ct), ct);
    }

    private void DrawResults(QueryResponse result)
    {
        if (result.Results.Count == 0)
        {
            ImGui.TextDisabled($"Nothing found for \"{result.Query.Term}\".");
            DrawProviderFooter(result);
            return;
        }

        foreach (var hit in result.Results)
        {
            switch (hit)
            {
                case EntityCardResult card:
                    DrawEntityCard(card);
                    break;
                case WikiPageResult wiki:
                    DrawWikiResult(wiki);
                    break;
            }

            ImGui.Spacing();
        }

        DrawProviderFooter(result);
    }

    private void DrawEntityCard(EntityCardResult card)
    {
        switch (card.Entity)
        {
            case NpcEntity npc:
                Header(npc.Name, "NPC");
                if (npc.Location is { } loc)
                {
                    ImGui.TextDisabled($"{loc.ZoneName} ({loc.MapX:0.0}, {loc.MapY:0.0})");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Flag map##npc{npc.RowId}"))
                        MapLinkOpener.Open(loc);
                }
                break;

            case ItemEntity item:
                Header(item.Name, item.Category.Length > 0 ? item.Category : "Item");
                if (item.Description.Length > 0)
                    ImGui.TextWrapped(item.Description);
                break;

            case QuestEntity quest:
                Header(quest.Name, quest.Genre.Length > 0 ? $"Quest · {quest.Genre}" : "Quest");
                if (quest.ClassJobLevel > 0)
                    ImGui.TextDisabled($"Level {quest.ClassJobLevel}");
                if (quest.Prerequisites.Count > 0)
                    ImGui.TextWrapped("Requires: " + string.Join(", ", quest.Prerequisites.Select(p => p.Name)));
                break;

            case MountEntity mount:
                Header(mount.Name, "Mount");
                break;

            case MinionEntity minion:
                Header(minion.Name, "Minion");
                break;

            case AchievementEntity achievement:
                Header(achievement.Name,
                    achievement.Category.Length > 0 ? $"Achievement · {achievement.Category}" : "Achievement");
                if (achievement.Description.Length > 0)
                    ImGui.TextWrapped(achievement.Description);
                break;

            default:
                Header(card.Title, card.Source.Label);
                break;
        }
    }

    private static void Header(string name, string tag)
    {
        ImGui.TextUnformatted(name);
        ImGui.SameLine();
        ImGui.TextDisabled(tag);
    }

    private void DrawWikiResult(WikiPageResult wiki)
    {
        ImGui.TextUnformatted(wiki.Title);
        ImGui.SameLine();
        ImGui.TextDisabled(wiki.Source.Label);
        if (wiki.Snippet != null)
            ImGui.TextWrapped(wiki.Snippet);
    }

    private static void DrawProviderFooter(QueryResponse result)
    {
        foreach (var provider in result.ProviderDetail)
        {
            if (provider.Status == ProviderStatus.Failed)
            {
                ImGui.Separator();
                ImGui.TextDisabled($"{provider.ProviderId} unavailable ({provider.Error}) - results may be incomplete.");
            }
        }
    }
}
