using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class RequestClientActivityTests
{
    [Theory]
    [InlineData("/internal-admin/users/sessions/revoke")]
    [InlineData("/api/v1/internal-admin/users/access-blocks")]
    [InlineData("/API/V1/INTERNAL-ADMIN/users/access-blocks")]
    public void Classifier_ExcludesServiceToServiceEndpoints(string path)
        => Assert.False(RelevantRequestClassifier.IsRelevant(new PathString(path)));

    [Theory]
    [InlineData("/internal-administrator")]
    [InlineData("/wp-login.php")]
    [InlineData("/.env")]
    public void Classifier_KeepsLookalikeAndScannerPaths(string path)
        => Assert.True(RelevantRequestClassifier.IsRelevant(new PathString(path)));

    [Theory]
    [InlineData(" 203.0.113.10 ", "203.0.113.10")]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10")]
    [InlineData("2001:DB8:0:0::1", "2001:db8::1")]
    public void Address_IsNormalized(string value, string expected)
    {
        Assert.True(RequestClientAddress.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData("203.0.113.")]
    [InlineData("203.0.113.0/24")]
    [InlineData("fe80::1%3")]
    [InlineData("not-an-address")]
    public void Address_RejectsAnythingButACompleteAddress(string? value)
        => Assert.False(RequestClientAddress.TryNormalize(value, out _));

    [Theory]
    [InlineData("", "/")]
    [InlineData("/wp-login.php", "/wp-login.php")]
    [InlineData("/api/v1/accounts/95a8be08-bb94-4515-80b2-b1e628f3145f/entries/42", "/api/v1/accounts/{id}/entries/{id}")]
    [InlineData("/a\nb", "/a?b")]
    public void Path_CollapsesIdentifiersAndControlCharacters(string path, string expected)
        => Assert.Equal(expected, RequestEndpointDescription.NormalizePath(path));

    [Fact]
    public void Path_IsTruncated()
        => Assert.Equal(RequestEndpointDescription.MaximumPathLength + 1, RequestEndpointDescription.NormalizePath("/" + new string('a', 500)).Length);

    [Theory]
    [InlineData("get", "GET")]
    [InlineData("DELETE", "DELETE")]
    [InlineData("A|B", "OTHER")]
    public void Method_IsLimitedToKnownValues(string method, string expected)
        => Assert.Equal(expected, RequestEndpointDescription.NormalizeMethod(method));

    [Fact]
    public async Task DisabledStore_ReturnsUnavailableAlignedBuckets()
    {
        await using var provider = CreateProvider(null);
        var reader = provider.GetRequiredService<IRequestClientActivityReader>();
        var from = new DateTimeOffset(2026, 10, 7, 12, 2, 0, TimeSpan.Zero);

        var activity = await reader.QueryAsync(from, from.AddMinutes(6));
        var details = await reader.GetDetailsAsync("203.0.113.10", from, from.AddMinutes(6));

        Assert.False(activity.IsAvailable);
        Assert.Equal(2, activity.Buckets.Count);
        Assert.False(details.IsAvailable);
    }

    [RequestDetectionFact]
    public async Task Middleware_RecordsAddressEndpointStatusAndUser()
    {
        await using var harness = await Harness.CreateAsync();
        var address = harness.NewAddress();
        var user = Guid.NewGuid();

        await harness.SendAsync(address, "GET", "/wp-login.php", 404);
        await harness.SendAsync(address, "GET", "/wp-login.php", 404);
        await harness.SendAsync(address, "POST", "/accounts/95a8be08-bb94-4515-80b2-b1e628f3145f", 200, user);
        await harness.SendAsync(address, "GET", "/internal-admin/users/sessions/revoke", 204);
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.SendAsync(address, "GET", "/boom", 200, throws: true));

        var activity = await harness.WaitForClientAsync(address, 4);
        var client = Assert.Single(activity.Clients, item => item.ClientIpAddress == address.ToString());
        Assert.Equal(3, client.FailedRequests);
        var bucket = Assert.Single(activity.Buckets, item => item.SelectedClientRequests > 0);
        Assert.Equal(4, bucket.SelectedClientRequests);
        Assert.Equal(3, bucket.SelectedClientFailedRequests);

        var details = await harness.Reader.GetDetailsAsync(address.ToString(), harness.FromUtc, harness.ToUtc);
        Assert.True(details.IsAvailable);
        Assert.Equal([user], details.UserIds);
        Assert.Equal(3, details.Endpoints.Count);
        var scanned = details.Endpoints[0];
        Assert.Equal(("GET", "/wp-login.php", 404, 2L), (scanned.Method, scanned.Path, scanned.StatusCode, scanned.Requests));
        Assert.Equal("Security.Tests", scanned.Application);
        Assert.Contains(details.Endpoints, item => item is { Method: "POST", Path: "/accounts/{id}", StatusCode: 200 });
        Assert.Contains(details.Endpoints, item => item is { Path: "/boom", StatusCode: 500 });
        Assert.DoesNotContain(details.Endpoints, item => item.Path.Contains("internal-admin", StringComparison.Ordinal));
    }

    [RequestDetectionFact]
    public async Task Details_DoNotIncludeAnAddressThatOnlySharesAPrefix()
    {
        await using var harness = await Harness.CreateAsync();
        var octet = RandomNumberGenerator.GetInt32(1, 25);
        var shorter = IPAddress.Parse($"203.0.113.{octet}");
        var longer = IPAddress.Parse($"203.0.113.{octet}5");

        await harness.SendAsync(shorter, "GET", "/shorter", 200);
        await harness.SendAsync(longer, "GET", "/longer", 200);
        await harness.WaitForClientAsync(longer, 1);
        await harness.WaitForClientAsync(shorter, 1);

        var details = await harness.Reader.GetDetailsAsync(shorter.ToString(), harness.FromUtc, harness.ToUtc);
        Assert.Equal("/shorter", Assert.Single(details.Endpoints).Path);
    }

    [RequestDetectionFact]
    public async Task EndpointLimit_KeepsOneScannerFromHidingOtherAddresses()
    {
        await using var harness = await Harness.CreateAsync();
        var scanner = harness.NewAddress();
        var bystander = harness.NewAddress();
        var over = RequestClientActivityService.MaximumEndpointFieldsPerClient + 25;

        for (var index = 0; index < over; index++) await harness.SendAsync(scanner, "GET", $"/probe-{index}", 404);
        await harness.SendAsync(bystander, "GET", "/transactions", 200);
        await harness.WaitForClientAsync(scanner, over);
        await harness.WaitForClientAsync(bystander, 1);

        var scanned = await harness.Reader.GetDetailsAsync(scanner.ToString(), harness.FromUtc, harness.ToUtc);
        var other = await harness.Reader.GetDetailsAsync(bystander.ToString(), harness.FromUtc, harness.ToUtc);
        Assert.Equal(RequestClientActivityService.MaximumEndpointFieldsPerClient, scanned.Endpoints.Count);
        Assert.Equal(25, scanned.OmittedRequests);
        Assert.Equal("/transactions", Assert.Single(other.Endpoints).Path);
        Assert.False(other.IsPossiblyIncomplete);
    }

    [RequestDetectionFact]
    public async Task ClientLimit_CountsRequestsBeyondItAsOmitted()
    {
        await using var harness = await Harness.CreateAsync();
        var addresses = Enumerable.Range(0, RequestClientActivityService.MaximumClientFieldsPerBucket + 10).Select(_ => harness.NewAddress()).ToArray();

        foreach (var address in addresses) await harness.SendAsync(address, "GET", "/", 200);

        RequestClientActivityResult activity = null!;
        await Harness.WaitForAsync(async () =>
        {
            activity = await harness.Reader.QueryAsync(harness.FromUtc, harness.ToUtc);
            return activity.TotalClients + activity.OmittedRequests == addresses.Length;
        });
        Assert.Equal(RequestClientActivityService.MaximumClientFieldsPerBucket, activity.TotalClients);
        Assert.Equal(10, activity.OmittedRequests);
    }

    private static ServiceProvider CreateProvider(string? connectionString, TimeProvider? timeProvider = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["RequestTelemetry:ConnectionString"] = connectionString }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRequestTelemetry(configuration);
        services.PostConfigure<RequestTelemetryOptions>(options => options.TimeoutMilliseconds = 10_000);
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        return services.BuildServiceProvider();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PocketLedger.Security.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly IHostedService worker;
        private readonly DateTimeOffset now;

        private Harness(ServiceProvider provider, IHostedService worker, DateTimeOffset now)
        {
            this.provider = provider;
            this.worker = worker;
            this.now = now;
            Reader = provider.GetRequiredService<IRequestClientActivityReader>();
        }

        public IRequestClientActivityReader Reader { get; }
        public DateTimeOffset FromUtc => now.AddMinutes(-1);
        public DateTimeOffset ToUtc => now.AddMinutes(1);

        // Every test writes to its own otherwise unused hour in the future, so runs against the same Valkey share neither buckets nor limits.
        public static async Task<Harness> CreateAsync()
        {
            var hoursAhead = RandomNumberGenerator.GetInt32(24, 24 * 365);
            var now = RequestTelemetryBuckets.Start(DateTimeOffset.UtcNow.AddHours(hoursAhead), 60).AddMinutes(30);
            var provider = CreateProvider(Environment.GetEnvironmentVariable("PL_TEST_VALKEY"), new FixedTimeProvider(now));
            var worker = provider.GetServices<IHostedService>().Single();
            await worker.StartAsync(CancellationToken.None);
            return new Harness(provider, worker, now);
        }

        public IPAddress NewAddress()
        {
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes.AsSpan(4));
            bytes[0] = 0x20; bytes[1] = 0x01; bytes[2] = 0x0d; bytes[3] = 0xb8;
            return new IPAddress(bytes);
        }

        public async Task SendAsync(IPAddress address, string method, string path, int status, Guid? userId = null, bool throws = false)
        {
            var context = new DefaultHttpContext { RequestServices = provider };
            context.Connection.RemoteIpAddress = address;
            context.Request.Method = method;
            context.Request.Path = path;
            if (userId is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())], "Test"));
            var middleware = new RequestTelemetryMiddleware(inner =>
            {
                if (throws) throw new InvalidOperationException("Simulated failure.");
                inner.Response.StatusCode = status;
                return Task.CompletedTask;
            }, provider.GetRequiredService<RequestTelemetryService>(), provider.GetRequiredService<SuspiciousRequestDetectionService>(), provider.GetRequiredService<TimeProvider>(), RequestTelemetryClientFilter.None);
            await middleware.InvokeAsync(context);
        }

        public async Task<RequestClientActivityResult> WaitForClientAsync(IPAddress address, long expectedRequests)
        {
            RequestClientActivityResult result = null!;
            await WaitForAsync(async () =>
            {
                result = await Reader.QueryAsync(FromUtc, ToUtc, address.ToString());
                return result.Clients.Any(item => item.ClientIpAddress == address.ToString() && item.TotalRequests == expectedRequests);
            });
            return result;
        }

        public static async Task WaitForAsync(Func<Task<bool>> condition)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (await condition()) return;
                await Task.Delay(50);
            }
            Assert.Fail("Client activity did not reach the expected state before the timeout.");
        }

        public async ValueTask DisposeAsync()
        {
            await worker.StopAsync(CancellationToken.None);
            await provider.DisposeAsync();
        }
    }
}
