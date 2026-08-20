using System.Diagnostics;
using System.Net;
using Wikiway.Core.Abstractions;
using Wikiway.Core.Wiki;
using Xunit;

namespace Wikiway.Core.Tests;

public class CachingHandlerTests
{
    [Fact]
    public async Task SecondRequestWithinTtlIsServedFromCache()
    {
        var inner = new CountingHandler("payload");
        var http = new HttpClient(new CachingHandler(new InMemoryCache()) { InnerHandler = inner });

        await http.GetStringAsync("https://example.com/api?q=1");
        var second = await http.GetStringAsync("https://example.com/api?q=1");

        Assert.Equal(1, inner.Calls);
        Assert.Equal("payload", second);
    }

    [Fact]
    public async Task ExpiredEntryIsRefetched()
    {
        var cache = new InMemoryCache();
        cache.Entries["https://example.com/api?q=1"] =
            new CacheEntry("stale", DateTimeOffset.UtcNow - TimeSpan.FromDays(2));
        var inner = new CountingHandler("fresh");
        var http = new HttpClient(new CachingHandler(cache) { InnerHandler = inner });

        var body = await http.GetStringAsync("https://example.com/api?q=1");

        Assert.Equal(1, inner.Calls);
        Assert.Equal("fresh", body);
    }

    [Fact]
    public async Task DifferentUrlsGetDifferentEntries()
    {
        var inner = new CountingHandler("x");
        var http = new HttpClient(new CachingHandler(new InMemoryCache()) { InnerHandler = inner });

        await http.GetStringAsync("https://example.com/api?q=1");
        await http.GetStringAsync("https://example.com/api?q=2");

        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public void ParseRequestsGetTheLongTtl()
    {
        Assert.Equal(TimeSpan.FromHours(24),
            CachingHandler.DefaultTtl(new Uri("https://x/api.php?action=parse&page=Y")));
        Assert.Equal(TimeSpan.FromHours(6),
            CachingHandler.DefaultTtl(new Uri("https://x/api.php?action=query&list=search")));
    }
}

public class ThrottlingHandlerTests
{
    [Fact]
    public async Task RapidRequestsAreSpacedOut()
    {
        var inner = new CountingHandler("ok");
        var http = new HttpClient(new ThrottlingHandler(TimeSpan.FromMilliseconds(200)) { InnerHandler = inner });

        var watch = Stopwatch.StartNew();
        await http.GetStringAsync("https://example.com/1");
        await http.GetStringAsync("https://example.com/2");
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds >= 150, $"only took {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task RetriesTooManyRequestsWithBackoff()
    {
        var inner = new CountingHandler("ok") { FailuresBeforeSuccess = 2, FailureStatus = HttpStatusCode.TooManyRequests };
        var http = new HttpClient(new ThrottlingHandler(TimeSpan.Zero, TimeSpan.FromMilliseconds(10))
        {
            InnerHandler = inner,
        });

        var body = await http.GetStringAsync("https://example.com/x");

        Assert.Equal(3, inner.Calls);
        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task RetryAfterHeaderStretchesTheBackoff()
    {
        var inner = new CountingHandler("ok")
        {
            FailuresBeforeSuccess = 1,
            FailureStatus = HttpStatusCode.TooManyRequests,
            RetryAfter = TimeSpan.FromMilliseconds(250),
        };
        var http = new HttpClient(new ThrottlingHandler(TimeSpan.Zero, TimeSpan.FromMilliseconds(10))
        {
            InnerHandler = inner,
        });

        var watch = Stopwatch.StartNew();
        var body = await http.GetStringAsync("https://example.com/x");
        watch.Stop();

        Assert.Equal("ok", body);
        Assert.Equal(2, inner.Calls);
        Assert.True(watch.ElapsedMilliseconds >= 200, $"only waited {watch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task GivesUpAfterThreeAttempts()
    {
        var inner = new CountingHandler("ok") { FailuresBeforeSuccess = 99, FailureStatus = HttpStatusCode.ServiceUnavailable };
        var http = new HttpClient(new ThrottlingHandler(TimeSpan.Zero, TimeSpan.FromMilliseconds(10))
        {
            InnerHandler = inner,
        });

        using var response = await http.GetAsync("https://example.com/x");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Calls);
    }
}

internal sealed class CountingHandler(string body) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public int FailuresBeforeSuccess { get; init; }
    public HttpStatusCode FailureStatus { get; init; } = HttpStatusCode.InternalServerError;
    public TimeSpan? RetryAfter { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var failing = Calls <= FailuresBeforeSuccess;
        var response = new HttpResponseMessage(failing ? FailureStatus : HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        };
        if (failing && RetryAfter is { } retryAfter)
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
        return Task.FromResult(response);
    }
}

internal sealed class InMemoryCache : ICacheStore
{
    public Dictionary<string, CacheEntry> Entries { get; } = [];

    public Task<CacheEntry?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(Entries.TryGetValue(key, out var e) ? e : null);

    public Task SetAsync(string key, string content, CancellationToken ct)
    {
        Entries[key] = new CacheEntry(content, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        Entries.Clear();
        return Task.CompletedTask;
    }
}
