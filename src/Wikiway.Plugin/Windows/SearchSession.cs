using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wikiway.Core.Models;

namespace Wikiway.Plugin.Windows;

// State of the main window's current search: the input text, the pending task,
// the response split into its display lists, and which rows and blocks the user
// has expanded. Reset on each new result (see MainWindow.Harvest).
internal sealed class SearchSession : IDisposable
{
    public string QueryInput = string.Empty;
    public string? Submitted;
    public readonly List<SearchResult> AboveGate = [];
    public readonly List<SearchResult> BelowGate = [];
    public readonly List<SearchResult> Wiki = [];
    public readonly HashSet<string> ExpandedRows = [];
    public readonly HashSet<(uint QuestRowId, int ChainIndex)> ExpandedChains = [];
    public readonly HashSet<string> ExpandedScenes = [];
    public readonly Dictionary<string, List<SceneGroup>> SceneGroups = [];
    // Item row id -> its ACQUISITION/USAGE load, started when a card showing
    // that item is first opened (MainWindow.LoadedItemDetails).
    public readonly Dictionary<uint, Task<ItemDetails>> ItemDetails = [];
    public bool LowRelevanceOpen;
    public bool MoreOpen;
    public bool WikiOpen;
    public bool HasGameResult;
    public CancellationTokenSource? Cts;
    public Task<QueryResponse>? Pending;
    public QueryResponse? Response;
    public string? Error;
    public bool ScrollToTop;

    public void Dispose()
    {
        Cts?.Cancel();
        Cts?.Dispose();
    }
}

internal sealed record SceneGroup(int Order, string Expansion, List<CutsceneAppearance> Scenes);
