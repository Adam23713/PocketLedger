using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PocketLedger.Security;
using StackExchange.Redis;

namespace PocketLedger.Security.Tests;

public sealed class RequestDetectionFactAttribute : FactAttribute
{
    public RequestDetectionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PL_TEST_VALKEY"))) Skip = "Set PL_TEST_VALKEY to run suspicious request detection integration tests.";
    }
}

public sealed class SuspiciousRequestDetectionTests
{
    private static int nextAddressSuffix = Random.Shared.Next(1, 200);

    [Fact]
    public async Task DisabledStore_IsUnavailableAndRecordingDoesNotAffectTheRequest()
    {
        var service = new SuspiciousRequestDetectionService(Options.Create(new RequestTelemetryOptions()), NullLogger<SuspiciousRequestDetectionService>.Instance);
        using var services = CreateRequestServices();
        var context = CreateContext(services, IPAddress.Parse("203.0.113.10"), Guid.NewGuid());

        service.TryRecord(context, DateTimeOffset.UtcNow);
        var result = await service.GetRecentAsync();

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Events);
        await service.DisposeAsync();
    }

    [RequestDetectionFact]
    public async Task TwoHundredRequestsDoNotTriggerButTheNextRequestCreatesAndThenMergesOneIncident()
    {
        await using var harness = await DetectionHarness.CreateAsync();
        var address = harness.NewAddress();
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        for (var index = 0; index < 100; index++) harness.Record(address, firstUser, timestamp.AddMilliseconds(index), "198.51.100.250");
        for (var index = 100; index < 200; index++) harness.Record(address, secondUser, timestamp.AddMilliseconds(index), "198.51.100.250");
        await harness.WaitForRollingCountAsync(address, 200);

        Assert.DoesNotContain((await harness.Reader.GetRecentAsync(100)).Events, item => item.ClientIpAddress == address.ToString());

        harness.Record(address, null, timestamp.AddMilliseconds(200), "198.51.100.250");
        var incident = await harness.WaitForIncidentAsync(address, 201, expectedUserCount: 2);

        Assert.Equal(address.ToString(), incident.ClientIpAddress);
        Assert.Equal(TimeSpan.FromMinutes(3), incident.Window);
        Assert.Equal(new[] { firstUser, secondUser }.Order().ToArray(), incident.UserIds.Order().ToArray());
        Assert.Contains("PocketLedger.Security.Tests", incident.Applications);
        Assert.Contains("identity.test", incident.Hosts);
        Assert.DoesNotContain("198.51.100.250", incident.ClientIpAddress);

        harness.Record(address, firstUser, timestamp.AddMilliseconds(201), "198.51.100.250");
        var merged = await harness.WaitForIncidentAsync(address, 202, expectedUserCount: 2);
        Assert.Equal(incident.DetectedAtUtc, merged.DetectedAtUtc);
        Assert.Single((await harness.Reader.GetRecentAsync(100)).Events, item => item.ClientIpAddress == address.ToString());
    }

    [RequestDetectionFact]
    public async Task RequestsOutsideTheRollingWindowExpireBeforeThresholdEvaluation()
    {
        await using var harness = await DetectionHarness.CreateAsync();
        var address = harness.NewAddress();
        var timestamp = DateTimeOffset.UtcNow;

        for (var index = 0; index < 200; index++) harness.Record(address, null, timestamp.AddMilliseconds(index));
        await harness.WaitForRollingCountAsync(address, 200);
        harness.Record(address, null, timestamp.AddMinutes(3).AddMilliseconds(201));
        await harness.WaitForRollingCountAsync(address, 1);

        Assert.DoesNotContain((await harness.Reader.GetRecentAsync(100)).Events, item => item.ClientIpAddress == address.ToString());
    }

    private sealed class DetectionHarness : IAsyncDisposable
    {
        private readonly SuspiciousRequestDetectionService service;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task worker;
        private readonly ServiceProvider requestServices;
        private readonly IConnectionMultiplexer redis;

        private DetectionHarness(SuspiciousRequestDetectionService service, ServiceProvider requestServices, IConnectionMultiplexer redis)
        {
            this.service = service;
            this.requestServices = requestServices;
            this.redis = redis;
            Reader = service;
            worker = service.RunAsync(cancellation.Token);
        }

        public ISuspiciousRequestEventReader Reader { get; }

        public static async Task<DetectionHarness> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("PL_TEST_VALKEY")!;
            var service = new SuspiciousRequestDetectionService(Options.Create(new RequestTelemetryOptions { ConnectionString = connectionString }), NullLogger<SuspiciousRequestDetectionService>.Instance);
            var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
            return new DetectionHarness(service, CreateRequestServices(), redis);
        }

        public IPAddress NewAddress() => IPAddress.Parse($"203.0.113.{Interlocked.Increment(ref nextAddressSuffix) % 250 + 1}");

        public void Record(IPAddress address, Guid? userId, DateTimeOffset timestamp, string? forwardedFor = null)
            => service.TryRecord(CreateContext(requestServices, address, userId, forwardedFor), timestamp);

        public Task WaitForRollingCountAsync(IPAddress address, long expected) => WaitForAsync(async () => await redis.GetDatabase().SortedSetLengthAsync(RollingKey(address)) == expected);

        public async Task<SuspiciousRequestEvent> WaitForIncidentAsync(IPAddress address, long expectedCount, int expectedUserCount = 0)
        {
            SuspiciousRequestEvent? match = null;
            await WaitForAsync(async () =>
            {
                match = (await Reader.GetRecentAsync(100)).Events.SingleOrDefault(item => item.ClientIpAddress == address.ToString() && item.RequestCount == expectedCount && item.UserIds.Count >= expectedUserCount);
                return match is not null;
            });
            return match!;
        }

        public async ValueTask DisposeAsync()
        {
            service.Complete();
            cancellation.Cancel();
            try { await worker; } catch (OperationCanceledException) { }
            await service.DisposeAsync();
            await redis.DisposeAsync();
            await requestServices.DisposeAsync();
            cancellation.Dispose();
        }

        private static RedisKey RollingKey(IPAddress address)
            => $"pocketledger:suspicious-requests:v1:rolling:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.ToString())))}";

        private static async Task WaitForAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await condition()) return;
                await Task.Delay(25);
            }
            Assert.Fail("The suspicious request detector did not reach the expected state before the timeout.");
        }
    }

    private static ServiceProvider CreateRequestServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext CreateContext(IServiceProvider services, IPAddress address, Guid? userId, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = address;
        context.Request.Host = new HostString("identity.test");
        if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (userId is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Test"));
        }
        return context;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PocketLedger.Security.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
