using System.Net;

namespace Wikiway.Core.Wiki;

public sealed class ThrottlingHandler : DelegatingHandler
{
    private readonly TimeSpan minInterval;
    private readonly TimeSpan backoffBase;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset lastSend = DateTimeOffset.MinValue;

    public ThrottlingHandler(TimeSpan? minInterval = null, TimeSpan? backoffBase = null)
    {
        this.minInterval = minInterval ?? TimeSpan.FromSeconds(1);
        this.backoffBase = backoffBase ?? TimeSpan.FromSeconds(2);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var wait = lastSend + minInterval - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                lastSend = DateTimeOffset.UtcNow;
            }
            finally
            {
                gate.Release();
            }

            var response = await base.SendAsync(Clone(request), ct).ConfigureAwait(false);
            if (attempt >= 2 || !IsRetryable(response.StatusCode))
                return response;

            response.Dispose();
            await Task.Delay(backoffBase * (attempt + 1), ct).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway;

    // The same HttpRequestMessage can't be sent twice, and ours are all GETs
    // with no body, so a shallow copy is enough.
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
