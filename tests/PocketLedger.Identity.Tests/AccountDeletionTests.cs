using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class AccountDeletionTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly Guid CurrentUserId = Guid.Parse("090aee3b-6fd5-4844-a5d3-f9ce916d56ba");
    private static readonly DateTimeOffset CurrentTimeUtc = new(2026, 10, 27, 15, 30, 0, TimeSpan.Zero);
    private readonly WebApplicationFactory<Program> factory;
    private readonly RecordingUserDataClient userDataClient = new();
    private readonly RecordingUserAccessPropagation accessPropagation = new();

    public AccountDeletionTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("OpenIddict:WebBaseUrl", "https://web.example");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(AccountDeletionTests)); options.UseOpenIddict(); });
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
                services.RemoveAll<IAdminUserDataClient>();
                services.AddSingleton<IAdminUserDataClient>(userDataClient);
                services.RemoveAll<IUserAccessPropagation>();
                services.AddSingleton<IUserAccessPropagation>(accessPropagation);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(CurrentTimeUtc));
            });
        });
    }

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Manage_ShowsAccountDetailsDeletionConfirmationAndBackupOption()
    {
        await SeedUserAsync();
        using var client = CreateClient();

        var response = await client.GetAsync("/Account/Manage");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("account-user", html);
        Assert.Contains("account@example.com", html);
        Assert.Contains("href=\"https://web.example/ImportExport#encrypted-backup\"", html);
        Assert.Contains("name=\"confirmation\"", html);
        Assert.Contains("Type <strong>DELETE</strong> to confirm", html);
        Assert.Contains("data-delete-account-submit disabled", html);
        Assert.Contains("/js/account-deletion.js", html);
        Assert.Contains("href=\"/Account/Manage\"", html);
    }

    [Fact]
    public async Task DeleteAccount_RejectsIncorrectConfirmationWithoutSideEffects()
    {
        await SeedUserAsync();
        using var client = CreateClient();

        var response = await PostDeleteAsync(client, "delete");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Manage", response.Headers.Location?.OriginalString);
        Assert.Null(userDataClient.DeletedUserId);
        Assert.Empty(accessPropagation.BlockedUserIds);
        await using var scope = factory.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(CurrentUserId.ToString());
        Assert.NotNull(user);
        Assert.False(user.IsAccessBlocked);
    }

    [Fact]
    public async Task DeleteAccount_RemovesCurrentUserDataAndIdentityAndKeepsTombstone()
    {
        await SeedUserAsync();
        await using (var auditScope = factory.Services.CreateAsyncScope())
        {
            var auditDbContext = auditScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            auditDbContext.AuthenticationAuditEvents.Add(new AuthenticationAuditEvent
            {
                Id = Guid.NewGuid(), UserId = CurrentUserId, TimestampUtc = CurrentTimeUtc, EventType = "LoginSucceeded", Outcome = "Success",
                RequestPath = "/", HttpMethod = "GET"
            });
            await auditDbContext.SaveChangesAsync();
        }
        using var client = CreateClient();

        var response = await PostDeleteAsync(client, "DELETE");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Equal(CurrentUserId, userDataClient.DeletedUserId);
        Assert.Contains(CurrentUserId, accessPropagation.BlockedUserIds);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(CurrentUserId.ToString()));
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var tombstone = await dbContext.DeletedUserTombstones.SingleAsync(item => item.UserId == CurrentUserId);
        Assert.Equal(CurrentTimeUtc, tombstone.DeletedAtUtc);
        Assert.False(await dbContext.AuthenticationAuditEvents.AnyAsync(item => item.UserId == CurrentUserId));
    }

    [Fact]
    public async Task BootstrapAdministrator_CannotDeleteOwnAccount()
    {
        await SeedUserAsync(isBootstrapAdministrator: true);
        using var client = CreateClient();

        var page = await client.GetStringAsync("/Account/Manage");
        var response = await PostDeleteAsync(client, "DELETE");

        Assert.Contains("bootstrap administrator account is protected", page);
        Assert.DoesNotContain("id=\"delete-account-form\"", page);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(userDataClient.DeletedUserId);
        Assert.Empty(accessPropagation.BlockedUserIds);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(CurrentUserId.ToString()));
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task SeedUserAsync(bool isBootstrapAdministrator = false)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        var user = isBootstrapAdministrator ? ApplicationUser.CreateBootstrapAdministrator("account-user") : new ApplicationUser { UserName = "account-user" };
        user.Id = CurrentUserId;
        user.NormalizedUserName = "ACCOUNT-USER";
        user.Email = "account@example.com";
        user.NormalizedEmail = "ACCOUNT@EXAMPLE.COM";
        user.TwoFactorEnabled = true;
        user.AuthenticatorSetupComplete = true;
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PostDeleteAsync(HttpClient client, string confirmation)
    {
        var html = await client.GetStringAsync("/Account/Manage");
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return await client.PostAsync("/Account/DeleteAccount", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["confirmation"] = confirmation,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value)
        }));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AccountDeletionTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, CurrentUserId.ToString()),
                new Claim(ClaimTypes.Name, "account-user")
            ], IdentityConstants.ApplicationScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class RecordingUserDataClient : IAdminUserDataClient
    {
        public Guid? DeletedUserId { get; private set; }

        public Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            DeletedUserId = userId;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUserAccessPropagation : IUserAccessPropagation
    {
        public List<Guid> BlockedUserIds { get; } = [];

        public Task BlockAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            BlockedUserIds.Add(userId);
            return Task.CompletedTask;
        }

        public Task UnblockAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SynchronizeAsync(IReadOnlyCollection<Guid> blockedUserIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
