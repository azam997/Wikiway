using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Caching;

public sealed class FileCacheStore : ICacheStore
{
    private readonly string directory;

    public FileCacheStore(string directory)
    {
        this.directory = directory;
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
        catch
        {
            // A corrupt or unreadable entry is just a miss.
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
        catch
        {
            // Failing to cache never fails the request.
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
        catch
        {
        }

        return Task.CompletedTask;
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
