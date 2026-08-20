using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Wikiway.Core.Models;

namespace Wikiway.Plugin.Windows;

internal sealed class SearchSession : IDisposable
{
    public string QueryInput = string.Empty;
    public readonly List<SearchResult> AboveGate = [];
    public readonly List<SearchResult> BelowGate = [];
    public readonly HashSet<string> ExpandedRows = [];
    public readonly HashSet<uint> ExpandedChains = [];
    public readonly HashSet<string> ExpandedScenes = [];
    public bool LowRelevanceOpen;
    public CancellationTokenSource? Cts;
    public Task<QueryResponse>? Pending;
    public QueryResponse? Response;
    public string? Error;
    public float ScrollY;
    public float? PendingScroll;

    public void Dispose()
    {
        Cts?.Cancel();
        Cts?.Dispose();
    }
}
