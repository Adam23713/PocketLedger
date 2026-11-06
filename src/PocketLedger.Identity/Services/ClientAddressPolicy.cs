using System.Net;

namespace PocketLedger.Services;

public enum ClientAddressKind { Public, Internal, CloudflareEdge }

// Decides which addresses may never be banned. Banning a Cloudflare edge would block every visitor routed through it,
// and a banned internal address would only ever match the application's own containers.
public static class ClientAddressPolicy
{
    // Keep in sync with trusted_proxies in Caddyfile.example; a test compares the two.
    public static readonly IReadOnlyList<IPNetwork> CloudflareNetworks = Parse(
        "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22", "104.16.0.0/13", "104.24.0.0/14", "108.162.192.0/18", "131.0.72.0/22", "141.101.64.0/18", "162.158.0.0/15",
        "172.64.0.0/13", "173.245.48.0/20", "188.114.96.0/20", "190.93.240.0/20", "197.234.240.0/22", "198.41.128.0/17",
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32", "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32");

    private static readonly IReadOnlyList<IPNetwork> NonPublicNetworks = Parse(
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16", "172.16.0.0/12", "192.168.0.0/16", "224.0.0.0/4", "240.0.0.0/4",
        "::/128", "::1/128", "fc00::/7", "fe80::/10", "ff00::/8");

    public static ClientAddressKind Classify(string address)
    {
        var parsed = IPAddress.Parse(address);
        if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
        if (NonPublicNetworks.Any(network => network.Contains(parsed))) return ClientAddressKind.Internal;
        return CloudflareNetworks.Any(network => network.Contains(parsed)) ? ClientAddressKind.CloudflareEdge : ClientAddressKind.Public;
    }

    public static string? GetBanRefusal(string address, bool isCurrentAdministrator)
    {
        if (isCurrentAdministrator) return $"{address} is the address you are using right now. Banning it would lock you out of the dashboard.";
        return Classify(address) switch
        {
            ClientAddressKind.Internal => $"{address} is an internal network address and cannot be banned.",
            ClientAddressKind.CloudflareEdge => $"{address} belongs to Cloudflare. Banning it would block every visitor routed through that edge. If real visitors appear with Cloudflare addresses, check X-Forwarded-For in the deployed Caddyfile.",
            _ => null
        };
    }

    public static bool IsCoveredBy(string address, string bannedAddressOrRange)
    {
        if (string.Equals(address, bannedAddressOrRange, StringComparison.OrdinalIgnoreCase)) return true;
        return IPNetwork.TryParse(bannedAddressOrRange, out var network) && IPAddress.TryParse(address, out var parsed) && network.Contains(parsed);
    }

    private static IReadOnlyList<IPNetwork> Parse(params string[] networks) => networks.Select(IPNetwork.Parse).ToArray();
}
