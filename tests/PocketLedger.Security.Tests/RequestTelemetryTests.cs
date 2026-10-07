using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class RequestTelemetryTests
{
    [Theory]
    [InlineData("/Transactions", true)]
    [InlineData("/api/transactions", true)]
    [InlineData("/health", false)]
    [InlineData("/health/ready", false)]
    [InlineData("/.well-known/openid-configuration", false)]
    [InlineData("/openapi/v1.json", false)]
    [InlineData("/swagger/index.html", false)]
    [InlineData("/css/site.css", false)]
    [InlineData("/images/logo.SVG", false)]
    public void RelevantRequestClassifier_ExcludesOnlyDefinedTechnicalTraffic(string path, bool expected)
        => Assert.Equal(expected, RelevantRequestClassifier.IsRelevant(new PathString(path)));

    [Fact]
    public void BucketStart_UsesFiveMinuteUtcBoundaries()
    {
        var timestamp = new DateTimeOffset(2026, 10, 7, 14, 17, 59, TimeSpan.FromHours(2));

        var bucket = RequestTelemetryBuckets.Start(timestamp);

        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 15, 0, TimeSpan.Zero), bucket);
    }

    [Fact]
    public async Task DisabledStore_ReturnsAlignedUnavailableEmptyBuckets()
    {
        await using var provider = CreateProvider(new Dictionary<string, string?>());
        var reader = provider.GetRequiredService<IRequestTelemetryReader>();

        var result = await reader.QueryAsync(
            new DateTimeOffset(2026, 10, 7, 12, 2, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 7, 12, 8, 0, TimeSpan.Zero));

        Assert.False(result.IsAvailable);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), result.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 12, 10, 0, TimeSpan.Zero), result.ToUtc);
        Assert.Equal(2, result.Buckets.Count);
        Assert.All(result.Buckets, bucket => Assert.Equal(0, bucket.TotalRequests));
        Assert.Equal(0, result.AverageRequestsPerMinute);
    }

    [Fact]
    public async Task Query_RejectsInvalidAndOverRetentionIntervals()
    {
        await using var provider = CreateProvider(new Dictionary<string, string?>());
        var reader = provider.GetRequiredService<IRequestTelemetryReader>();
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentException>(() => reader.QueryAsync(now, now));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => reader.QueryAsync(now.AddHours(-48).AddTicks(-1), now));
    }

    [Fact]
    public async Task UnreachableStore_ReturnsUnavailableWithoutThrowing()
    {
        await using var provider = CreateProvider(new Dictionary<string, string?>
        {
            ["RequestTelemetry:ConnectionString"] = "127.0.0.1:1,abortConnect=false,connectTimeout=100,asyncTimeout=100,connectRetry=0"
        });
        var reader = provider.GetRequiredService<IRequestTelemetryReader>();

        var result = await reader.QueryAsync(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);

        Assert.False(result.IsAvailable);
        Assert.Equal(0, result.TotalRequests);
    }

    [Fact]
    public async Task Registration_UsesTheSameSingletonForReaderAndBackgroundWorker()
    {
        await using var provider = CreateProvider(new Dictionary<string, string?>());

        var reader = provider.GetRequiredService<IRequestTelemetryReader>();
        var worker = provider.GetServices<IHostedService>().Single();

        Assert.Same(reader, worker);
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequestTelemetry(configuration);
        return services.BuildServiceProvider();
    }
}
