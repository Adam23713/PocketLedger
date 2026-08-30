namespace PocketLedger.Web.Authentication;

public sealed class IdentityBackchannelHandler(Uri publicAuthority, Uri internalBaseAddress) : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } uri && string.Equals(uri.Host, publicAuthority.Host, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = new Uri(internalBaseAddress, uri.PathAndQuery.TrimStart('/'));
        }
        return base.SendAsync(request, cancellationToken);
    }
}
