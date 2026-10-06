using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

// One source of results: LocalGameDataProvider (game sheets) or
// ConsoleGamesWikiProvider (the wiki). QueryOrchestrator runs every available
// provider for each query.
public interface ISearchProvider
{
    string Id { get; }

    bool IsAvailable { get; }

    Task<ProviderResult> SearchAsync(NormalizedQuery query, CancellationToken ct);
}

public enum ProviderStatus
{
    Ok,
    Failed,
    Skipped,
}

// What one provider returned for one query, including whether it failed or was
// skipped, so the UI can say a source was unavailable.
public sealed record ProviderResult(
    string ProviderId,
    IReadOnlyList<SearchResult> Results,
    ProviderStatus Status,
    string? Error = null)
{
    public static ProviderResult Failure(string providerId, string error) =>
        new(providerId, [], ProviderStatus.Failed, error);

    public static ProviderResult Skip(string providerId) =>
        new(providerId, [], ProviderStatus.Skipped);
}
