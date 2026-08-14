using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using System.Net;
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
    public async Task RateLimiter_ReplenishesAfterWindow()
    {
        using var limiter = CreateLimiter(1);
        for (var i = 0; i < 3; i++) using (var lease = await limiter.AcquireAsync("adam", default)) Assert.True(lease.IsAcquired);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        using var replenished = await limiter.AcquireAsync("adam", default);
        Assert.True(replenished.IsAcquired);
    }

    [Fact]
    public void DefaultMaximumUserCount_IsOne()
    {
        Assert.Equal(1, new AccountManagementOptions().MaximumUserCount);
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
