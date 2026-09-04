using PocketLedger.Models;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class UserDateProviderTests
{
    [Fact]
    public void Today_UsesRequestedZoneAcrossUtcMidnight()
    {
        var provider = CreateProvider(new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 15), provider.Today("Pacific/Kiritimati"));
        Assert.Equal(new DateOnly(2026, 8, 14), provider.Today("Pacific/Honolulu"));
    }

    [Fact]
    public void ToUtc_UsesDaylightSavingOffset()
    {
        var result = CreateProvider(DateTimeOffset.UnixEpoch).ToUtc(new DateOnly(2026, 7, 15), new TimeOnly(12, 0), "Europe/Budapest");

        Assert.Equal(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ToUtc_UsesStandardOffset()
    {
        var result = CreateProvider(DateTimeOffset.UnixEpoch).ToUtc(new DateOnly(2026, 1, 15), new TimeOnly(12, 0), "Europe/Budapest");

        Assert.Equal(new DateTimeOffset(2026, 1, 15, 11, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ToUtc_UsesStandardOffsetForAmbiguousLocalTime()
    {
        var result = CreateProvider(DateTimeOffset.UnixEpoch).ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30), "Europe/Budapest");

        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void ToUtc_RejectsInvalidLocalTime()
    {
        var provider = CreateProvider(DateTimeOffset.UnixEpoch);

        Assert.Throws<BusinessRuleException>(() => provider.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), "Europe/Budapest"));
    }

    [Fact]
    public void NormalizeTimeZoneId_RejectsUnknownZone()
    {
        Assert.Throws<BusinessRuleException>(() => CreateProvider(DateTimeOffset.UnixEpoch).NormalizeTimeZoneId("Not/A-Time-Zone"));
    }

    private static UserDateProvider CreateProvider(DateTimeOffset now) => new(new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
