using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Controllers;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class AdminDashboardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid CurrentUserId = Guid.Parse("95a8be08-bb94-4515-80b2-b1e628f3145f");
    private static readonly DateTimeOffset CurrentTimeUtc = new(2026, 10, 8, 12, 2, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> factory;
    private readonly TestRequestTelemetryReader requestTelemetry = new();

    public AdminDashboardTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("RequestTelemetry:ConnectionString", "");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(AdminDashboardTests)); options.UseOpenIddict(); });
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.RemoveAll<IRequestTelemetryReader>();
                services.AddSingleton<IRequestTelemetryReader>(requestTelemetry);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(CurrentTimeUtc));
                services.RemoveAll<ICrowdSecDecisionReader>();
                services.AddSingleton<ICrowdSecDecisionReader>(new TestCrowdSecDecisionReader());
            });
        });
    }

    [Fact]
    public void AdminControllerAndActions_ExplicitlyRequireBootstrapAdministratorPolicy()
    {
        var controllerPolicy = typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>().Single().Policy;
        var indexPolicy = typeof(AdminController).GetMethod(nameof(AdminController.Index), BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttributes<AuthorizeAttribute>().Single().Policy;
        var requestActivityPolicy = typeof(AdminController).GetMethod(nameof(AdminController.RequestActivity), BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttributes<AuthorizeAttribute>().Single().Policy;

        Assert.Equal(BootstrapAdministratorAuthorization.PolicyName, controllerPolicy);
        Assert.Equal(BootstrapAdministratorAuthorization.PolicyName, indexPolicy);
        Assert.Equal(BootstrapAdministratorAuthorization.PolicyName, requestActivityPolicy);
    }

    [Fact]
    public async Task Index_RejectsAuthenticatedUserWithoutBootstrapAdministratorClaim()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Index_ReturnsTenDeterministicallyOrderedUsersWithoutSearch()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await SendAsAdministratorAsync(client, "/Admin?page=1");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        for (var index = 11; index >= 2; index--) Assert.Contains($"<tr><td>user-{index:00}</td>", content);
        Assert.DoesNotContain("<tr><td>user-01</td>", content);
        Assert.DoesNotContain("never-signed-in", content);
        Assert.True(content.IndexOf("user-11", StringComparison.Ordinal) < content.IndexOf("user-10", StringComparison.Ordinal));
        Assert.DoesNotContain("type=\"search\"", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page 1 of 2", content);
        Assert.Contains("203.0.113.99", content);
        Assert.Contains("pocketledger/http-flood", content);
    }

    [Fact]
    public async Task Index_ClampsInvalidPageAndShowsNeverSignedInStateOnLastPage()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var firstPage = await (await SendAsAdministratorAsync(client, "/Admin?page=0")).Content.ReadAsStringAsync();
        var lastPage = await (await SendAsAdministratorAsync(client, "/Admin?page=999")).Content.ReadAsStringAsync();

        Assert.Contains("Page 1 of 2", firstPage);
        Assert.Contains("Page 2 of 2", lastPage);
        Assert.Contains("<tr><td>user-01</td>", lastPage);
        Assert.Contains("never-signed-in", lastPage);
        Assert.Contains("Never signed in", lastPage);
        Assert.DoesNotContain("<tr><td>user-02</td>", lastPage);
    }

    [Fact]
    public async Task RequestActivity_RejectsAuthenticatedUserWithoutBootstrapAdministratorClaim()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin/RequestActivity");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(requestTelemetry.LastQuery);
    }

    [Fact]
    public async Task RequestActivity_AcceptsOnlySupportedLookbacksAndDefaultsToTwentyFourHours()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        foreach (var hours in new[] { 3, 6, 12, 24, 48 })
        {
            var response = await SendAsAdministratorAsync(client, $"/Admin/RequestActivity?hours={hours}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(CurrentTimeUtc.AddHours(-hours), requestTelemetry.LastQuery?.FromUtc);
            Assert.Equal(CurrentTimeUtc, requestTelemetry.LastQuery?.ToUtc);
        }

        var defaultResponse = await SendAsAdministratorAsync(client, "/Admin/RequestActivity");
        var invalidResponse = await SendAsAdministratorAsync(client, "/Admin/RequestActivity?hours=4");

        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.Equal(CurrentTimeUtc.AddHours(-24), requestTelemetry.LastQuery?.FromUtc);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task RequestActivity_ReturnsAggregateFiveMinuteRatesAndAnonymousContext()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await SendAsAdministratorAsync(client, "/Admin/RequestActivity");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(root.GetProperty("isPartial").GetBoolean());
        Assert.False(root.GetProperty("isEmpty").GetBoolean());
        Assert.Equal(10, root.GetProperty("seriesRequests").GetInt64());
        Assert.Equal(2, root.GetProperty("anonymousRequests").GetInt64());
        Assert.Equal(2d, root.GetProperty("points")[0].GetProperty("averageRequestsPerMinute").GetDouble());
        Assert.Contains(root.GetProperty("users").EnumerateArray(), user => user.GetProperty("id").GetGuid() == CurrentUserId && user.GetProperty("username").GetString() == "user-01");
    }

    [Fact]
    public async Task RequestActivity_ReturnsSelectedUserFiveMinuteRates()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await SendAsAdministratorAsync(client, $"/Admin/RequestActivity?hours=3&userId={CurrentUserId}");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CurrentUserId, requestTelemetry.LastQuery?.UserId);
        Assert.Equal("user-01", root.GetProperty("seriesLabel").GetString());
        Assert.Equal(4, root.GetProperty("seriesRequests").GetInt64());
        Assert.Equal(.8d, root.GetProperty("points")[0].GetProperty("averageRequestsPerMinute").GetDouble());
        Assert.Equal(4, root.GetProperty("points")[0].GetProperty("selectedUserRequests").GetInt64());
    }

    [Fact]
    public async Task RequestActivity_ReportsUnavailableAndPartialTelemetryStates()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        requestTelemetry.IsAvailable = false;

        var unavailableResponse = await SendAsAdministratorAsync(client, "/Admin/RequestActivity");
        Assert.Equal(HttpStatusCode.OK, unavailableResponse.StatusCode);
        using var unavailable = JsonDocument.Parse(await unavailableResponse.Content.ReadAsStringAsync());
        requestTelemetry.IsAvailable = true;
        requestTelemetry.IsPartial = true;
        var partialResponse = await SendAsAdministratorAsync(client, "/Admin/RequestActivity");
        Assert.Equal(HttpStatusCode.OK, partialResponse.StatusCode);
        using var partial = JsonDocument.Parse(await partialResponse.Content.ReadAsStringAsync());

        Assert.False(unavailable.RootElement.GetProperty("isAvailable").GetBoolean());
        Assert.True(partial.RootElement.GetProperty("isAvailable").GetBoolean());
        Assert.True(partial.RootElement.GetProperty("isPartial").GetBoolean());
    }

    private async Task SeedUsersAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Users.AddRange(Enumerable.Range(1, 11).Select(index => new ApplicationUser
        {
            Id = index == 1 ? CurrentUserId : Guid.NewGuid(), UserName = $"user-{index:00}", NormalizedUserName = $"USER-{index:00}", LastSuccessfulLoginAtUtc = start.AddHours(index),
            TwoFactorEnabled = index == 1, AuthenticatorSetupComplete = index == 1
        }));
        dbContext.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "never-signed-in", NormalizedUserName = "NEVER-SIGNED-IN" });
        await dbContext.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> SendAsAdministratorAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthenticationHandler.AdministratorHeader, "true");
        return client.SendAsync(request);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AdminDashboardTest";
        public const string AdministratorHeader = "X-Test-Bootstrap-Administrator";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, CurrentUserId.ToString()), new(ClaimTypes.Name, "user-01") };
            if (Request.Headers[AdministratorHeader] == "true") claims.Add(new Claim(BootstrapAdministratorAuthorization.ClaimType, bool.TrueString));
            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class TestCrowdSecDecisionReader : ICrowdSecDecisionReader
    {
        public Task<CrowdSecBanDecisionsResult> GetActiveBansAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CrowdSecBanDecisionsResult(true, [new CrowdSecBanDecision("203.0.113.99", "Ip", "pocketledger/http-flood", "crowdsec", "ban", "30m")]));
    }

    private sealed class TestRequestTelemetryReader : IRequestTelemetryReader
    {
        public bool IsAvailable { get; set; } = true;
        public bool IsPartial { get; set; }
        public TelemetryQuery? LastQuery { get; private set; }

        public Task<RequestTelemetryStatistics> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? userId = null, CancellationToken cancellationToken = default)
        {
            LastQuery = new TelemetryQuery(fromUtc, toUtc, userId);
            var alignedFromUtc = RequestTelemetryBuckets.Start(fromUtc);
            var alignedToUtc = RequestTelemetryBuckets.Start(toUtc.AddMinutes(5).AddTicks(-1));
            var buckets = new List<RequestTelemetryBucketSnapshot>();
            for (var bucketStart = alignedFromUtc; bucketStart < alignedToUtc; bucketStart = bucketStart.AddMinutes(5))
            {
                var hasActivity = buckets.Count == 0;
                buckets.Add(new RequestTelemetryBucketSnapshot(bucketStart, hasActivity ? 10 : 0, hasActivity ? 8 : 0, hasActivity ? 2 : 0,
                    userId is null ? null : hasActivity ? 4 : 0));
            }
            if (IsPartial) buckets.RemoveAt(buckets.Count - 1);
            long? selectedRequests = userId is null ? null : 4;
            return Task.FromResult(new RequestTelemetryStatistics(IsAvailable, alignedFromUtc, alignedToUtc, userId, 10, 8, 2, selectedRequests,
                (selectedRequests ?? 10) / (alignedToUtc - alignedFromUtc).TotalMinutes, buckets));
        }
    }

    private sealed record TelemetryQuery(DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? UserId);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
