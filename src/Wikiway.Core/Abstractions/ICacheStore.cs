namespace Wikiway.Core.Abstractions;

public interface ICacheStore
{
    Task<CacheEntry?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string content, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}

public sealed record CacheEntry(string Content, DateTimeOffset StoredAt);
