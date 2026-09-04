using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;

namespace PocketLedger.Services;

public interface IAuthenticationRateLimiter
{
    ValueTask<RateLimitLease> AcquireAsync(string? username, CancellationToken cancellationToken);
}

public sealed class AuthenticationRateLimiter(IOptions<AuthenticationSecurityOptions> options) : IAuthenticationRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, FixedWindowRateLimiter> limiters = new(StringComparer.Ordinal);
    private readonly AuthenticationSecurityOptions settings = options.Value;

    public ValueTask<RateLimitLease> AcquireAsync(string? username, CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(username) ? "<EMPTY>" : username.Trim().ToUpperInvariant();
        var limiter = limiters.GetOrAdd(key, _ => new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.RateLimitPermitCount, Window = TimeSpan.FromSeconds(settings.RateLimitWindowSeconds), QueueLimit = 0, AutoReplenishment = true
        }));
        return limiter.AcquireAsync(1, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var limiter in limiters.Values) limiter.Dispose();
    }
}
