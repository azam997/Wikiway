using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Caching;

public sealed class FileCacheStore : ICacheStore
{
    private readonly string directory;
    private readonly Action<string>? log;

    public FileCacheStore(string directory, Action<string>? log = null)
    {
        this.directory = directory;
        this.log = log;
    }

    public async Task<CacheEntry?> GetAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);

        try
        {
            if (!File.Exists(path))
                return null;

            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var envelope = JsonSerializer.Deserialize<Envelope>(json);
            return envelope?.Content == null ? null : new CacheEntry(envelope.Content, envelope.StoredAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // A corrupt or unreadable entry is just a miss.
            log?.Invoke($"cache read failed for {path}: {e.Message}");
            return null;
        }
    }

    public async Task SetAsync(string key, string content, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var envelope = new Envelope { StoredAt = DateTimeOffset.UtcNow, Content = content };
            await File.WriteAllTextAsync(PathFor(key), JsonSerializer.Serialize(envelope), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Failing to cache never fails the request.
            log?.Invoke($"cache write failed: {e.Message}");
        }
    }

    public Task ClearAsync(CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
                    File.Delete(file);
            }
        }
        catch (Exception e)
        {
            log?.Invoke($"cache clear failed: {e.Message}");
        }

        return Task.CompletedTask;
    }

    // TTLs are only enforced on read, so without a sweep every distinct query
    // leaves a file behind forever.
    public void Sweep(TimeSpan maxAge, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(directory))
                return;

            var cutoff = DateTime.UtcNow - maxAge;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                ct.ThrowIfCancellationRequested();
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            log?.Invoke($"cache sweep failed: {e.Message}");
        }
    }

    private string PathFor(string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(directory, hash + ".json");
    }

    private sealed class Envelope
    {
        public DateTimeOffset StoredAt { get; set; }
        public string? Content { get; set; }
    }
}
