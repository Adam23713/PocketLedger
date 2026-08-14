using System.Net;

namespace PocketLedger.Services;

public interface IClientIpAddressResolver
{
    string? GetClientIpAddress(HttpContext context);
    string? GetForwardedClientIpAddress(HttpContext context);
}

public sealed class ClientIpAddressResolver : IClientIpAddressResolver
{
    private static readonly string[] ForwardedIpHeaders = ["CF-Connecting-IP", "X-Forwarded-For", "X-Real-IP"];

    public string? GetClientIpAddress(HttpContext context) => GetForwardedClientIpAddress(context) ?? context.Connection.RemoteIpAddress?.ToString();

    public string? GetForwardedClientIpAddress(HttpContext context)
    {
        foreach (var headerName in ForwardedIpHeaders)
        {
            foreach (var value in context.Request.Headers[headerName].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IPAddress.TryParse(value, out var address)) return address.ToString();
            }
        }

        return null;
    }
}
