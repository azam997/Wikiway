namespace Wikiway.Core.Abstractions;

// Persistent key/value store for raw wiki API responses (FileCacheStore on
// disk). Entries carry their store time so callers can apply their own expiry.
public interface ICacheStore
{
    Task<CacheEntry?> GetAsync(string key, CancellationToken ct);

    Task SetAsync(string key, string content, CancellationToken ct);

    Task ClearAsync(CancellationToken ct);
}

public sealed record CacheEntry(string Content, DateTimeOffset StoredAt);
