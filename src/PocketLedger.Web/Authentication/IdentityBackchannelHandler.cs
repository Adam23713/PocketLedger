namespace PocketLedger.Web.Authentication;

public sealed class IdentityBackchannelHandler(Uri publicAuthority, Uri internalBaseAddress) : HttpClientHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } uri && string.Equals(uri.Host, publicAuthority.Host, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = new Uri(internalBaseAddress, uri.PathAndQuery.TrimStart('/'));
        }

        if (request.Method != HttpMethod.Get) return await base.SendAsync(request, cancellationToken);
        for (var attempt = 1; ; attempt++)
        {
            using var retryRequest = CloneGetRequest(request);
            try
            {
                return await base.SendAsync(retryRequest, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 30 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    private static HttpRequestMessage CloneGetRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version, VersionPolicy = request.VersionPolicy };
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        return clone;
    }
}
