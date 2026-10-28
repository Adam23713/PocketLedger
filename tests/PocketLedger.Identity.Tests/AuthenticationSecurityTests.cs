using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.RateLimiting;
using PocketLedger.Configuration;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class AuthenticationSecurityTests
{
    [Fact]
    public async Task RateLimiter_AllowsThreeRequestsAndRejectsFourth()
    {
        using var limiter = CreateLimiter(60);
        for (var i = 0; i < 3; i++) using (var lease = await limiter.AcquireAsync("adam", default)) Assert.True(lease.IsAcquired);
        using var rejected = await limiter.AcquireAsync("adam", default);
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public async Task RateLimiter_UsernameCasingAndWhitespaceUseSamePartition()
    {
        using var limiter = CreateLimiter(60);
        using var first = await limiter.AcquireAsync(" Adam ", default);
        using var second = await limiter.AcquireAsync("ADAM", default);
        using var third = await limiter.AcquireAsync("adam", default);
        using var rejected = await limiter.AcquireAsync("aDaM", default);
        Assert.True(first.IsAcquired && second.IsAcquired && third.IsAcquired);
        Assert.False(rejected.IsAcquired);
    }

    [Fact]
    public async Task RateLimiter_HandlesMissingAndWhitespaceOnlyUsernames()
    {
        using var limiter = CreateLimiter(60);
        using var missing = await limiter.AcquireAsync(null, default);
        using var whitespace = await limiter.AcquireAsync("   ", default);
        Assert.True(missing.IsAcquired);
        Assert.True(whitespace.IsAcquired);
    }

    [Fact]
    public async Task RateLimiter_ReplenishesAfterWindow()
    {
        using var limiter = CreateLimiter(1);
        for (var i = 0; i < 3; i++) using (var lease = await limiter.AcquireAsync("adam", default)) Assert.True(lease.IsAcquired);
        // Replenishment runs on a timer that can fire late on a busy CI runner, so poll until a generous deadline instead of a single fixed delay.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        RateLimitLease replenished;
        while (!(replenished = await limiter.AcquireAsync("adam", default)).IsAcquired && DateTime.UtcNow < deadline)
        {
            replenished.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        using (replenished) Assert.True(replenished.IsAcquired);
    }

    [Fact]
    public void ClientIpAddress_UsesConfiguredHeaderPriorityAndFirstValidAddress()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["CF-Connecting-IP"] = "invalid, 203.0.113.10";
        context.Request.Headers["X-Forwarded-For"] = "198.51.100.20";

        Assert.Equal("203.0.113.10", new ClientIpAddressResolver().GetClientIpAddress(context));
    }

    [Fact]
    public void ClientIpAddress_FallsBackToRemoteAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.30");

        Assert.Equal("192.0.2.30", new ClientIpAddressResolver().GetClientIpAddress(context));
    }

    private static AuthenticationRateLimiter CreateLimiter(int windowSeconds) => new(Options.Create(new AuthenticationSecurityOptions { RateLimitPermitCount = 3, RateLimitWindowSeconds = windowSeconds }));
}
