using System.Net;
using Wikiway.Core.Abstractions;

namespace Wikiway.Core.Wiki;

public sealed class CachingHandler : DelegatingHandler
{
    private readonly ICacheStore cache;
    private readonly Func<Uri, TimeSpan> ttl;

    public CachingHandler(ICacheStore cache, Func<Uri, TimeSpan>? ttl = null)
    {
        this.cache = cache;
        this.ttl = ttl ?? DefaultTtl;
    }

    // Page content moves slower than search relevance.
    public static TimeSpan DefaultTtl(Uri url) =>
        url.Query.Contains("action=parse") ? TimeSpan.FromHours(24) : TimeSpan.FromHours(6);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri == null)
            return await base.SendAsync(request, ct).ConfigureAwait(false);

        var key = request.RequestUri.ToString();
        var entry = await cache.GetAsync(key, ct).ConfigureAwait(false);
        if (entry != null && DateTimeOffset.UtcNow - entry.StoredAt < ttl(request.RequestUri))
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(entry.Content),
                RequestMessage = request,
            };
        }

        var response = await base.SendAsync(request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            await cache.SetAsync(key, body, ct).ConfigureAwait(false);
            response.Content = new StringContent(body);
        }

        return response;
    }
}
