using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace PocketLedger.Landing.Contact;

public interface IContactRateLimiter
{
    // Counted on every submission attempt, so invalid or failed submissions still use up the client's allowance.
    bool TryAcquireAttempt(string clientKey);

    // Counted only for messages that are about to be sent; protects the inbox from distributed floods.
    bool TryAcquireDelivery(string senderEmail);
}

// The landing page runs as a single instance, so in-memory windows are enough; Caddy and Cloudflare limit in front of it as well.
public sealed class ContactRateLimiter : IContactRateLimiter, IDisposable
{
    private const string GlobalKey = "global";
    private readonly PartitionedRateLimiter<string> attempts;
    private readonly PartitionedRateLimiter<string> senders;
    private readonly PartitionedRateLimiter<string> global;

    public ContactRateLimiter(IOptions<ContactOptions> options)
    {
        var limits = options.Value.Limits;
        attempts = Create(limits.PerClientPerHour, TimeSpan.FromHours(1));
        senders = Create(limits.PerSenderPerDay, TimeSpan.FromDays(1));
        global = Create(limits.GlobalPerHour, TimeSpan.FromHours(1));
    }

    public bool TryAcquireAttempt(string clientKey) => Acquire(attempts, clientKey);

    public bool TryAcquireDelivery(string senderEmail)
    {
        if (!Acquire(senders, senderEmail.ToUpperInvariant())) return false;
        return Acquire(global, GlobalKey);
    }

    // IPv6 clients usually control a whole /64, so they share one allowance.
    public static string GetClientKey(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();
        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    public void Dispose()
    {
        attempts.Dispose();
        senders.Dispose();
        global.Dispose();
    }

    private static bool Acquire(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Create(int permitLimit, TimeSpan window) => PartitionedRateLimiter.Create<string, string>(key =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, permitLimit), Window = window, QueueLimit = 0, AutoReplenishment = true
        }), StringComparer.Ordinal);
}
