using Wikiway.Core.Caching;
using Xunit;

namespace Wikiway.Core.Tests;

public class FileCacheStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "wikiway-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FileCacheStore store;

    public FileCacheStoreTests() => store = new FileCacheStore(dir);

    public void Dispose()
    {
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task SetThenGetRoundtrips()
    {
        await store.SetAsync("key", "hello", CancellationToken.None);

        var entry = await store.GetAsync("key", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("hello", entry.Content);
        Assert.True(DateTimeOffset.UtcNow - entry.StoredAt < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task MissingKeyIsNull()
    {
        Assert.Null(await store.GetAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task CorruptFileIsAMiss()
    {
        await store.SetAsync("key", "hello", CancellationToken.None);
        foreach (var file in Directory.EnumerateFiles(dir))
            await File.WriteAllTextAsync(file, "definitely not json {");

        Assert.Null(await store.GetAsync("key", CancellationToken.None));
    }

    [Fact]
    public async Task ClearRemovesEverything()
    {
        await store.SetAsync("a", "1", CancellationToken.None);
        await store.SetAsync("b", "2", CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.GetAsync("a", CancellationToken.None));
        Assert.Null(await store.GetAsync("b", CancellationToken.None));
    }

    [Fact]
    public async Task SweepDeletesOnlyFilesOlderThanMaxAge()
    {
        await store.SetAsync("old", "1", CancellationToken.None);
        var oldFile = Assert.Single(Directory.EnumerateFiles(dir));
        await store.SetAsync("fresh", "2", CancellationToken.None);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromDays(30));

        store.Sweep(TimeSpan.FromDays(7), CancellationToken.None);

        Assert.Null(await store.GetAsync("old", CancellationToken.None));
        Assert.NotNull(await store.GetAsync("fresh", CancellationToken.None));
    }
}
