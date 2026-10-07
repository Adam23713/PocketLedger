using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Identity.Tests;

public sealed class LoginEventsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid CurrentUserId = Guid.Parse("c37c93e2-9824-4245-a672-cc11a4802f52");
    private readonly WebApplicationFactory<Program> anonymousFactory;
    private readonly WebApplicationFactory<Program> authenticatedFactory;

    public LoginEventsTests(WebApplicationFactory<Program> factory)
    {
        anonymousFactory = ConfigureFactory(factory, authenticate: false);
        authenticatedFactory = ConfigureFactory(factory, authenticate: true);
    }

    [Fact]
    public async Task LoginEvents_RequiresAuthentication()
    {
        using var client = anonymousFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Account/LoginEvents");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Security_RequiresAuthentication()
    {
        using var client = anonymousFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Account/Security");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task LoginEvents_ReturnsOnlyTheCurrentUsersRequestedPage()
    {
        await SeedEventsAsync();
        using var client = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Account/LoginEvents?page=2");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Login Events</h1>", content);
        Assert.Contains("Current-00", content);
        Assert.DoesNotContain("Current-30", content);
        Assert.DoesNotContain("OtherUserSecret", content);
        Assert.Contains("page=1", content);
        Assert.DoesNotContain("page=3", content);
    }

    [Fact]
    public async Task Security_ShowsExactlyTheCurrentUsersFiveLatestEventsAndActions()
    {
        await SeedEventsAsync();
        using var client = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Account/Security");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1 class=\"mb-1\">Security</h1>", content);
        Assert.Contains("href=\"/Account/LoginEvents\"", content);
        Assert.Contains("href=\"/Account/ChangePassword\"", content);
        Assert.Contains("href=\"/Account/RecoveryCode\"", content);
        for (var index = 30; index >= 26; index--) Assert.Contains($"Current-{index:00}", content);
        Assert.DoesNotContain("Current-25", content);
        Assert.DoesNotContain("OtherUserSecret", content);
        Assert.True(content.IndexOf("Current-30", StringComparison.Ordinal) < content.IndexOf("Current-29", StringComparison.Ordinal));
        Assert.True(content.IndexOf("Current-29", StringComparison.Ordinal) < content.IndexOf("Current-28", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Security_ShowsClearEmptyHistoryState()
    {
        await SeedCurrentUserAsync();
        using var client = authenticatedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Account/Security");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No login events are available for your account yet.", content);
    }

    private static WebApplicationFactory<Program> ConfigureFactory(WebApplicationFactory<Program> factory, bool authenticate) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase($"{nameof(LoginEventsTests)}-{authenticate}"); options.UseOpenIddict(); });
            if (authenticate)
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
            }
        });
    });

    private async Task SeedEventsAsync()
    {
        await SeedCurrentUserAsync();
        await using var scope = authenticatedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.AuthenticationAuditEvents.AddRange(Enumerable.Range(0, 31).Select(index => new AuthenticationAuditEvent
        {
            Id = Guid.NewGuid(),
            UserId = CurrentUserId,
            TimestampUtc = start.AddMinutes(index),
            EventType = $"Current-{index:00}",
            Outcome = "Success",
            RequestPath = "/Account/Login",
            HttpMethod = "POST"
        }));
        dbContext.AuthenticationAuditEvents.Add(new AuthenticationAuditEvent
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TimestampUtc = start.AddDays(1),
            EventType = "OtherUserSecret",
            Outcome = "Success",
            RequestPath = "/Account/Login",
            HttpMethod = "POST"
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedCurrentUserAsync()
    {
        await using var scope = authenticatedFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        dbContext.Users.Add(new ApplicationUser { Id = CurrentUserId, UserName = "current-user", NormalizedUserName = "CURRENT-USER" });
        await dbContext.SaveChangesAsync();
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, CurrentUserId.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
