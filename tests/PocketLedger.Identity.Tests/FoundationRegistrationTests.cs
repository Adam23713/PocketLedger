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

namespace PocketLedger.Identity.Tests;

public sealed class FoundationRegistrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Password = "Registration!Password#2026";
    private readonly WebApplicationFactory<Program> factory;

    public FoundationRegistrationTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("Registration:Enabled", "true");
        builder.UseSetting("Authentication:RateLimitPermitCount", "20");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(FoundationRegistrationTests)); options.UseOpenIddict(); });
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, VerifiedExternalAuthenticationHandler>(VerifiedExternalAuthenticationHandler.SchemeName, "Verified provider", _ => { })
                .AddScheme<AuthenticationSchemeOptions, UnverifiedExternalAuthenticationHandler>(UnverifiedExternalAuthenticationHandler.SchemeName, "Unverified provider", _ => { });
        });
    });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task PasswordLogin_RedirectsBlockedUserToAccessBlockedPage()
    {
        await ResetDatabaseAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "blocked-user", Email = "blocked@example.test", IsAccessBlocked = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        }
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");

        var response = await client.PostAsync("/Account/Login", Form(new Dictionary<string, string>
        {
            ["Username"] = "blocked-user",
            ["Password"] = Password,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/AccessBlocked", response.Headers.Location?.OriginalString);
        Assert.Contains("currently disabled and under review", await client.GetStringAsync(response.Headers.Location));
    }

    [Fact]
    public async Task PasswordRegistration_CreatesUserAuditsSuccessAndStartsAuthenticatorOnboarding()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Register");

        var response = await client.PostAsync("/Account/Register", Form(new Dictionary<string, string>
        {
            ["Username"] = "new-user",
            ["Email"] = "new-user@example.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/SetupAuthenticator", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync("new-user"))!;
        Assert.Equal("new-user@example.test", user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.False(user.AuthenticatorSetupComplete);
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "Registration" && item.Outcome == "Success" && item.UserId == user.Id);
    }

    [Theory]
    [InlineData(null, "http://localhost:5050/Session/CompleteSignIn")]
    [InlineData("/connect/authorize?client_id=pocketledger-web", "/connect/authorize?client_id=pocketledger-web")]
    [InlineData("/Account/Security", "http://localhost:5050/Session/CompleteSignIn")]
    public async Task PasswordRegistration_CompletionRecordsLoginAndContinuesTheCorrectSessionFlow(string? returnUrl, string expectedLocation)
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("CF-Connecting-IP", "203.0.113.10");
        var registrationToken = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        var registrationResponse = await client.PostAsync("/Account/Register", Form(new Dictionary<string, string>
        {
            ["Username"] = "completed-user",
            ["Email"] = "completed-user@example.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
            ["ReturnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = registrationToken
        }));
        var setupHtml = await client.GetStringAsync(registrationResponse.Headers.Location);
        string authenticatorKey;
        await using (var setupScope = factory.Services.CreateAsyncScope())
        {
            var users = setupScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("completed-user"))!;
            authenticatorKey = (await users.GetAuthenticatorKeyAsync(user))!;
        }

        var setupResponse = await client.PostAsync("/Account/SetupAuthenticator", Form(new Dictionary<string, string>
        {
            ["Code"] = GenerateAuthenticatorCode(authenticatorKey),
            ["ReturnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(setupHtml)
        }));
        var recoveryCodesHtml = await setupResponse.Content.ReadAsStringAsync();
        var completionResponse = await client.PostAsync("/Account/RecoveryCodes", Form(new Dictionary<string, string>
        {
            ["Confirmed"] = "true",
            ["ReturnUrl"] = returnUrl ?? string.Empty,
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(recoveryCodesHtml)
        }));

        Assert.Equal(HttpStatusCode.Redirect, completionResponse.StatusCode);
        Assert.Equal(expectedLocation, completionResponse.Headers.Location?.OriginalString);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var completedUser = (await verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync("completed-user"))!;
        Assert.True(completedUser.AuthenticatorSetupComplete);
        Assert.NotNull(completedUser.LastSuccessfulLoginAtUtc);
        Assert.Equal("203.0.113.10", completedUser.LastSuccessfulLoginIpAddress);
        Assert.Contains(await verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "LoginSucceeded" && item.Outcome == "Success" && item.UserId == completedUser.Id);
    }

    [Fact]
    public async Task IdentityLogout_ContinuesThroughWebSessionCleanup()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var registrationToken = await GetAntiforgeryTokenAsync(client, "/Account/Register");
        var registrationResponse = await client.PostAsync("/Account/Register", Form(new Dictionary<string, string>
        {
            ["Username"] = "logout-user",
            ["Email"] = "logout-user@example.test",
            ["Password"] = Password,
            ["ConfirmPassword"] = Password,
            ["__RequestVerificationToken"] = registrationToken
        }));
        var setupHtml = await client.GetStringAsync(registrationResponse.Headers.Location);

        var logoutResponse = await client.PostAsync("/Account/Logout", Form(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(setupHtml)
        }));

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("http://localhost:5050/Session/CompleteLogout", logoutResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task LoginAndRegister_ShowOnlyConfiguredExternalProviders()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var loginHtml = await client.GetStringAsync("/Account/Login");
        var registerHtml = await client.GetStringAsync("/Account/Register");

        Assert.Contains("href=\"/Account/Register\"", loginHtml);
        Assert.Contains("Continue with Verified provider", loginHtml);
        Assert.Contains("Continue with Unverified provider", loginHtml);
        Assert.Contains("Continue with Verified provider", registerHtml);
    }

    [Fact]
    public async Task ExternalRegistration_RequiresVerifiedEmail()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();

        var response = await CompleteExternalLoginAsync(client, UnverifiedExternalAuthenticationHandler.SchemeName);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("must supply a verified email address", html);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().Users.ToListAsync());
    }

    [Fact]
    public async Task ExternalRegistration_RejectsExistingEmailWithoutLinking()
    {
        await ResetDatabaseAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(new ApplicationUser { Id = Guid.NewGuid(), UserName = "existing-user", Email = VerifiedExternalAuthenticationHandler.Email }, Password)).Succeeded);
        }
        using var client = CreateClient();

        var response = await CompleteExternalLoginAsync(client, VerifiedExternalAuthenticationHandler.SchemeName);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("already exists for this email address", html);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var usersAfter = verificationScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await usersAfter.Users.ToListAsync());
        Assert.Null(await usersAfter.FindByLoginAsync(VerifiedExternalAuthenticationHandler.SchemeName, VerifiedExternalAuthenticationHandler.Subject));
    }

    [Fact]
    public async Task ExternalRegistration_UsesProviderSubjectAndReturningLoginDoesNotDuplicateUser()
    {
        await ResetDatabaseAsync();
        using (var firstClient = CreateClient())
        {
            var firstResponse = await CompleteExternalLoginAsync(firstClient, VerifiedExternalAuthenticationHandler.SchemeName);
            Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
            Assert.StartsWith("/Account/SetupAuthenticator", firstResponse.Headers.Location?.OriginalString);
        }

        using (var returningClient = CreateClient())
        {
            var returningResponse = await CompleteExternalLoginAsync(returningClient, VerifiedExternalAuthenticationHandler.SchemeName);
            Assert.Equal(HttpStatusCode.Redirect, returningResponse.StatusCode);
            Assert.StartsWith("/Account/SetupAuthenticator", returningResponse.Headers.Location?.OriginalString);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var allUsers = await users.Users.ToListAsync();
        Assert.Single(allUsers);
        Assert.Equal(allUsers[0].Id, (await users.FindByLoginAsync(VerifiedExternalAuthenticationHandler.SchemeName, VerifiedExternalAuthenticationHandler.Subject))?.Id);
    }

    [Fact]
    public void IdentityModel_EnforcesUniqueNonNullNormalizedEmail()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var index = context.Model.FindEntityType(typeof(ApplicationUser))!.GetIndexes().Single(item => item.Properties.Select(property => property.Name).SequenceEqual([nameof(ApplicationUser.NormalizedEmail)]));

        Assert.True(index.IsUnique);
        Assert.Equal("\"NormalizedEmail\" IS NOT NULL", index.GetFilter());
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private async Task ResetDatabaseAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.EnsureDeletedAsync();
    }

    private static async Task<HttpResponseMessage> CompleteExternalLoginAsync(HttpClient client, string provider)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        var challenge = await client.PostAsync("/Account/ExternalLogin", Form(new Dictionary<string, string>
        {
            ["provider"] = provider,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.NotNull(challenge.Headers.Location);
        return await client.GetAsync(challenge.Headers.Location);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        return ExtractAntiforgeryToken(html);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
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

    private static FormUrlEncodedContent Form(Dictionary<string, string> values)
    {
        var content = new FormUrlEncodedContent(values);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }
}

public abstract class ExternalAuthenticationHandlerBase(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected abstract bool EmailVerified { get; }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, VerifiedExternalAuthenticationHandler.Subject),
            new(ClaimTypes.Email, VerifiedExternalAuthenticationHandler.Email),
            new("email_verified", EmailVerified.ToString())
        };
        await Context.SignInAsync(IdentityConstants.ExternalScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), properties);
        Response.Redirect(properties.RedirectUri!);
    }
}

public sealed class VerifiedExternalAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : ExternalAuthenticationHandlerBase(options, logger, encoder)
{
    public const string SchemeName = "VerifiedProvider";
    public const string Subject = "provider-subject-123";
    public const string Email = "external@example.test";
    protected override bool EmailVerified => true;
}

public sealed class UnverifiedExternalAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : ExternalAuthenticationHandlerBase(options, logger, encoder)
{
    public const string SchemeName = "UnverifiedProvider";
    protected override bool EmailVerified => false;
}
