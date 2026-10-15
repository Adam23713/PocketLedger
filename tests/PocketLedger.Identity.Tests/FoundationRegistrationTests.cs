using System.Net;
using System.Net.Http.Headers;
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

namespace PocketLedger.Identity.Tests;

public sealed class FoundationRegistrationTests : IClassFixture<WebApplicationFactory<Program>>
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
                .AddScheme<AuthenticationSchemeOptions, SecondVerifiedExternalAuthenticationHandler>(SecondVerifiedExternalAuthenticationHandler.SchemeName, "Second verified provider", _ => { })
                .AddScheme<AuthenticationSchemeOptions, UnverifiedExternalAuthenticationHandler>(UnverifiedExternalAuthenticationHandler.SchemeName, "Unverified provider", _ => { });
        });
    });

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
    public async Task DifferentProviderWithExistingEmail_IsRejectedWithoutAutomaticLinking()
    {
        await ResetDatabaseAsync();
        using (var firstClient = CreateClient())
        {
            var firstResponse = await CompleteExternalLoginAsync(firstClient, VerifiedExternalAuthenticationHandler.SchemeName);
            Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        }

        using var secondClient = CreateClient();
        var secondResponse = await CompleteExternalLoginAsync(secondClient, SecondVerifiedExternalAuthenticationHandler.SchemeName);
        var html = await secondResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Contains("already exists for this email address", html);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Single(await users.Users.ToListAsync());
        Assert.Null(await users.FindByLoginAsync(SecondVerifiedExternalAuthenticationHandler.SchemeName, SecondVerifiedExternalAuthenticationHandler.SubjectValue));
    }

    [Fact]
    public async Task PasswordRegistration_RemainsUsableWithExternalProvidersConfigured()
    {
        await ResetDatabaseAsync();
        using (var registrationClient = CreateClient())
        {
            var registrationToken = await GetAntiforgeryTokenAsync(registrationClient, "/Account/Register");
            var registrationResponse = await registrationClient.PostAsync("/Account/Register", Form(new Dictionary<string, string>
            {
                ["Username"] = "password-user",
                ["Email"] = "password-user@example.test",
                ["Password"] = Password,
                ["ConfirmPassword"] = Password,
                ["__RequestVerificationToken"] = registrationToken
            }));
            Assert.Equal(HttpStatusCode.Redirect, registrationResponse.StatusCode);
        }

        using var loginClient = CreateClient();
        var loginToken = await GetAntiforgeryTokenAsync(loginClient, "/Account/Login");
        var loginResponse = await loginClient.PostAsync("/Account/Login", Form(new Dictionary<string, string>
        {
            ["Username"] = "password-user",
            ["Password"] = Password,
            ["__RequestVerificationToken"] = loginToken
        }));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.StartsWith("/Account/SetupAuthenticator", loginResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ExternalOnlyAccount_SecurityPageDoesNotOfferPasswordChange()
    {
        await ResetDatabaseAsync();
        using var client = CreateClient();
        var registrationResponse = await CompleteExternalLoginAsync(client, VerifiedExternalAuthenticationHandler.SchemeName);
        Assert.Equal(HttpStatusCode.Redirect, registrationResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByLoginAsync(VerifiedExternalAuthenticationHandler.SchemeName, VerifiedExternalAuthenticationHandler.Subject))!;
            user.TwoFactorEnabled = true;
            user.AuthenticatorSetupComplete = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        }

        var html = await client.GetStringAsync("/Account/Security");

        Assert.Contains("does not have a PocketLedger password", html);
        Assert.DoesNotContain("href=\"/Account/ChangePassword\"", html);
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
}

public abstract class ExternalAuthenticationHandlerBase(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected abstract bool EmailVerified { get; }
    protected virtual string ProviderSubject => VerifiedExternalAuthenticationHandler.Subject;
    protected virtual string ProviderEmail => VerifiedExternalAuthenticationHandler.Email;

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, ProviderSubject),
            new(ClaimTypes.Email, ProviderEmail),
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

public sealed class SecondVerifiedExternalAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : ExternalAuthenticationHandlerBase(options, logger, encoder)
{
    public const string SchemeName = "SecondVerifiedProvider";
    public const string SubjectValue = "second-provider-subject-456";
    protected override bool EmailVerified => true;
    protected override string ProviderSubject => SubjectValue;
}
