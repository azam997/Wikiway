using Wikiway.Core.Models;

namespace Wikiway.Core.Abstractions;

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
