using System.Net;
using System.Net.Http.Headers;
using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
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
using PocketLedger.Security;

namespace PocketLedger.Identity.Tests;

public sealed class WaveTwoIdentityTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid CurrentUserId = Guid.Parse("d34a26aa-f46d-48ce-a9f7-fbf3f928dffe");
    private const string CurrentPassword = "Old!PocketLedger#2026";
    private readonly WebApplicationFactory<Program> factory;

    public WaveTwoIdentityTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("RequestTelemetry:ConnectionString", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(WaveTwoIdentityTests)); options.UseOpenIddict(); });
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    });

    [Fact]
    public async Task AuthenticatedNavigation_UsesConfiguredLinksAndShowsAdminOnlyForClaim()
    {
        await SeedUserAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var regularHtml = await client.GetStringAsync("/Account/LoginEvents");
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/Account/LoginEvents");
        adminRequest.Headers.Add(TestAuthenticationHandler.AdministratorHeader, "true");
        var adminHtml = await (await client.SendAsync(adminRequest)).Content.ReadAsStringAsync();

        Assert.Contains("<header class=\"app-header\">", regularHtml);
        Assert.Contains("href=\"http://localhost:5050\"", regularHtml);
        Assert.Contains("href=\"/Account/Security\"", regularHtml);
        Assert.Contains("href=\"/Account/LoginEvents\"", regularHtml);
        Assert.Contains("id=\"theme-menu-button\"", regularHtml);
        Assert.Contains("id=\"profile-menu-button\"", regularHtml);
        Assert.Contains("action=\"/Account/Logout\"", regularHtml);
        Assert.DoesNotContain("href=\"/Admin\"", regularHtml);
        Assert.Contains("href=\"/Admin\"", adminHtml);
    }

    [Fact]
    public async Task ChangePassword_UpdatesOnlyTheCurrentUsersPasswordAndAuditsSuccess()
    {
        await SeedUserAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        const string newPassword = "New!PocketLedger#2027";
        var token = await GetAntiforgeryTokenAsync(client, "/Account/ChangePassword");

        var response = await client.PostAsync("/Account/ChangePassword", Form(new Dictionary<string, string>
        {
            ["CurrentPassword"] = CurrentPassword,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/ChangePassword", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(CurrentUserId.ToString()))!;
        Assert.False(await users.CheckPasswordAsync(user, CurrentPassword));
        Assert.True(await users.CheckPasswordAsync(user, newPassword));
        var auditEvents = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync();
        Assert.Contains(auditEvents, item => item.EventType == "PasswordChange" && item.Outcome == "Success" && item.UserId == CurrentUserId);
        Assert.Contains(auditEvents, item => item.EventType == "SecurityStampInvalidation" && item.Outcome == "Success" && item.UserId == CurrentUserId);
    }

    [Fact]
    public async Task ChangePassword_RejectsWrongCurrentPasswordAndAuditsGenericFailure()
    {
        await SeedUserAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await GetAntiforgeryTokenAsync(client, "/Account/ChangePassword");

        var response = await client.PostAsync("/Account/ChangePassword", Form(new Dictionary<string, string>
        {
            ["CurrentPassword"] = "Wrong!PocketLedger#2026",
            ["NewPassword"] = "New!PocketLedger#2027",
            ["ConfirmPassword"] = "New!PocketLedger#2027",
            ["__RequestVerificationToken"] = token
        }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Password change was not completed", html);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.CheckPasswordAsync((await users.FindByIdAsync(CurrentUserId.ToString()))!, CurrentPassword));
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "PasswordChange" && item.Outcome == "Failure" && item.FailureReason == "InvalidCurrentPassword" && item.UserId == CurrentUserId);
    }

    [Fact]
    public async Task AuthenticatorRecovery_ConsumesCodeInvalidatesOldTotpAndStartsEnrollment()
    {
        var recoveryCode = await SeedUserWithAuthenticatorAsync();
        string oldTotp;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(CurrentUserId.ToString()))!;
            oldTotp = GenerateAuthenticatorCode((await users.GetAuthenticatorKeyAsync(user))!);
            Assert.True(await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, oldTotp));
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await GetAntiforgeryTokenAsync(client, "/Account/RecoveryCode");

        var response = await client.PostAsync("/Account/RecoveryCode", Form(new Dictionary<string, string>
        {
            ["Code"] = recoveryCode,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/SetupAuthenticator", response.Headers.Location?.OriginalString);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationUsers = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var recoveredUser = (await verificationUsers.FindByIdAsync(CurrentUserId.ToString()))!;
        Assert.False(recoveredUser.TwoFactorEnabled);
        Assert.False(recoveredUser.AuthenticatorSetupComplete);
        Assert.False((await verificationUsers.RedeemTwoFactorRecoveryCodeAsync(recoveredUser, recoveryCode)).Succeeded);
        Assert.False(await verificationUsers.VerifyTwoFactorTokenAsync(recoveredUser, verificationUsers.Options.Tokens.AuthenticatorTokenProvider, oldTotp));
        Assert.Equal(1, await verificationUsers.CountRecoveryCodesAsync(recoveredUser));
        var auditEvents = await verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync();
        Assert.Contains(auditEvents, item => item.EventType == "RecoveryCodeAuthentication" && item.Outcome == "Success" && item.UserId == CurrentUserId);
        Assert.Contains(auditEvents, item => item.EventType == "AuthenticatorResetThroughRecovery" && item.Outcome == "Success" && item.UserId == CurrentUserId);
    }

    [Fact]
    public async Task AuthenticatorRecovery_RejectsInvalidCodeWithoutChangingAuthenticator()
    {
        await SeedUserWithAuthenticatorAsync();
        string oldTotp;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(CurrentUserId.ToString()))!;
            oldTotp = GenerateAuthenticatorCode((await users.GetAuthenticatorKeyAsync(user))!);
            Assert.True(await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, oldTotp));
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var token = await GetAntiforgeryTokenAsync(client, "/Account/RecoveryCode");

        var response = await client.PostAsync("/Account/RecoveryCode", Form(new Dictionary<string, string>
        {
            ["Code"] = "malformed-recovery-code",
            ["__RequestVerificationToken"] = token
        }));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid authentication attempt.", html);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationUsers = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var userAfterFailure = (await verificationUsers.FindByIdAsync(CurrentUserId.ToString()))!;
        Assert.True(userAfterFailure.TwoFactorEnabled);
        Assert.True(userAfterFailure.AuthenticatorSetupComplete);
        Assert.True(await verificationUsers.VerifyTwoFactorTokenAsync(userAfterFailure, verificationUsers.Options.Tokens.AuthenticatorTokenProvider, oldTotp));
        Assert.Equal(2, await verificationUsers.CountRecoveryCodesAsync(userAfterFailure));
        Assert.Contains(await verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "RecoveryCodeAuthentication" && item.Outcome == "Failure" && item.FailureReason == "InvalidCode" && item.UserId == CurrentUserId);
    }

    private async Task SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.EnsureDeletedAsync();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = CurrentUserId, UserName = "wave-two-user", NormalizedUserName = "WAVE-TWO-USER", AuthenticatorSetupComplete = true };
        Assert.True((await users.CreateAsync(user, CurrentPassword)).Succeeded);
    }

    private async Task<string> SeedUserWithAuthenticatorAsync()
    {
        await SeedUserAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(CurrentUserId.ToString()))!;
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        user.AuthenticatorSetupComplete = true;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 2))!.First();
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values)
    {
        var content = new FormUrlEncodedContent(values);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    private static string GenerateAuthenticatorCode(string sharedKey)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        using var hmac = new HMACSHA1(DecodeBase32(sharedKey));
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character);
            if (index < 0) throw new FormatException("Invalid Base32 authenticator key.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return bytes.ToArray();
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "WaveTwoTest";
        public const string AdministratorHeader = "X-Test-Bootstrap-Administrator";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, CurrentUserId.ToString()),
                new(ClaimTypes.Name, "wave-two-user")
            };
            if (Request.Headers[AdministratorHeader] == "true") claims.Add(new Claim(BootstrapAdministratorAuthorization.ClaimType, bool.TrueString));
            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
