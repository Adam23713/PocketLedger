using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class GitHubAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string UserEndpoint = "https://api.github.com/user";
    private const string EmailsEndpoint = "https://api.github.com/user/emails";
    private const string UserJson = """{"id":12345,"login":"octocat","email":null}""";
    private readonly WebApplicationFactory<Program> factory;

    public GitHubAuthenticationTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Theory]
    [InlineData(null, null)]
    [InlineData("github-client-id", null)]
    [InlineData(null, "github-client-secret")]
    public async Task IncompleteConfiguration_DoesNotRegisterGitHubScheme(string? clientId, string? clientSecret)
    {
        using var configuredFactory = ConfigureFactory(clientId, clientSecret);

        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(GitHubAuthenticationEvents.SchemeName));
    }

    [Fact]
    public async Task CompleteConfiguration_RegistersMinimalGitHubSchemeAndMapsStableNumericSubject()
    {
        using var configuredFactory = ConfigureFactory("github-client-id", "github-client-secret");
        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<OAuthOptions>>().Get(GitHubAuthenticationEvents.SchemeName);

        var scheme = await schemes.GetSchemeAsync(GitHubAuthenticationEvents.SchemeName);
        using var userData = JsonDocument.Parse("""{"id":12345,"login":"octocat","name":"Mona","email":"profile@example.test"}""");
        var identity = new ClaimsIdentity();
        foreach (var action in options.ClaimActions) action.Run(userData.RootElement, identity, GitHubAuthenticationEvents.SchemeName);

        Assert.Equal("GitHub", scheme?.DisplayName);
        Assert.Equal("github-client-id", options.ClientId);
        Assert.Equal("github-client-secret", options.ClientSecret);
        Assert.Equal(["user:email"], options.Scope);
        Assert.Equal("/signin-github", options.CallbackPath);
        Assert.Equal("https://github.com/login/oauth/authorize", options.AuthorizationEndpoint);
        Assert.Equal("https://github.com/login/oauth/access_token", options.TokenEndpoint);
        Assert.Equal(UserEndpoint, options.UserInformationEndpoint);
        Assert.True(options.UsePkce);
        Assert.Equal("12345", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        // The unverifiable profile email and profile name must never become claims.
        Assert.Single(identity.Claims);
    }

    [Fact]
    public async Task LoginPage_ShowsGitHubButtonOnlyWhenConfigured()
    {
        using var configuredFactory = ConfigureFactory("github-client-id", "github-client-secret");
        using var unconfiguredFactory = ConfigureFactory(null, null);

        var configured = await configuredFactory.CreateClient().GetStringAsync("/Account/Login");
        var unconfigured = await unconfiguredFactory.CreateClient().GetStringAsync("/Account/Login");

        Assert.Contains("external-login-github", configured);
        Assert.Contains("Continue with GitHub", configured);
        Assert.DoesNotContain("external-login-github", unconfigured);
    }

    [Fact]
    public async Task CancelledProviderFlow_RedirectsToExternalLoginCallbackWithRemoteError()
    {
        using var configuredFactory = ConfigureFactory("github-client-id", "github-client-secret");
        using var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/signin-github?error=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/ExternalLoginCallback?remoteError=ProviderError", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CancelledProviderFlow_ShowsErrorOnLoginPage()
    {
        using var configuredFactory = ConfigureFactory("github-client-id", "github-client-secret");

        var html = await configuredFactory.CreateClient().GetStringAsync("/signin-github?error=access_denied");

        Assert.Contains("The external login was cancelled or rejected by the provider.", html);
    }

    [Fact]
    public async Task RemoteFailure_PreservesReturnUrlFromProtectedProperties()
    {
        var http = new DefaultHttpContext();
        var context = new RemoteFailureContext(http, Scheme(), new OAuthOptions(), new InvalidOperationException("denied")) { Properties = new AuthenticationProperties { RedirectUri = "/Account/ExternalLoginCallback?returnUrl=%2Fconnect" } };

        await ExternalLoginEvents.RemoteFailureAsync(context);

        Assert.True(context.Result?.Handled);
        Assert.Equal(StatusCodes.Status302Found, http.Response.StatusCode);
        Assert.Equal("/Account/ExternalLoginCallback?returnUrl=%2Fconnect&remoteError=ProviderError", http.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task CreatingTicket_UsesVerifiedPrimaryEmailEvenWhenProfileEmailIsHidden()
    {
        var handler = new StubHandler(UserJson, """[{"email":"other@example.test","primary":false,"verified":true},{"email":" primary@example.test ","primary":true,"verified":true}]""");

        var identity = await RunCreatingTicketAsync(handler);

        Assert.Equal("12345", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("primary@example.test", identity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(bool.TrueString, identity.FindFirst("email_verified")?.Value, ignoreCase: true);
        Assert.Equal(GitHubAuthenticationEvents.SchemeName, identity.FindFirst(ClaimTypes.Email)?.Issuer);
        Assert.Equal([UserEndpoint, EmailsEndpoint], handler.RequestedUris);
        Assert.All(handler.Authorizations, authorization => Assert.Equal("Bearer test-token", authorization));
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{"message":"unexpected"}""")]
    [InlineData("""[{"email":"unverified@example.test","primary":true,"verified":false}]""")]
    [InlineData("""[{"email":"secondary@example.test","primary":false,"verified":true}]""")]
    [InlineData("""[{"email":"not-an-email","primary":true,"verified":true}]""")]
    [InlineData("""[{"email":null,"primary":true,"verified":true}]""")]
    [InlineData("""[{"primary":true,"verified":true}]""")]
    [InlineData("""[{"email":"flags@example.test","primary":"true","verified":"true"}]""")]
    public async Task CreatingTicket_WithoutUsableVerifiedPrimaryEmail_KeepsSubjectButAddsNoEmailClaims(string emailsJson)
    {
        var identity = await RunCreatingTicketAsync(new StubHandler(UserJson, emailsJson));

        Assert.Equal("12345", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Null(identity.FindFirst(ClaimTypes.Email));
        Assert.Null(identity.FindFirst("email_verified"));
    }

    [Fact]
    public async Task CreatingTicket_WhenEmailsRequestFails_KeepsSubjectButAddsNoEmailClaims()
    {
        var identity = await RunCreatingTicketAsync(new StubHandler(UserJson, """{"message":"Forbidden"}""", emailsStatus: HttpStatusCode.Forbidden));

        Assert.Equal("12345", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Null(identity.FindFirst(ClaimTypes.Email));
    }

    [Fact]
    public async Task CreatingTicket_WhenUserRequestFails_AddsNoClaimsAndSkipsEmailsRequest()
    {
        var handler = new StubHandler("""{"message":"Bad credentials"}""", "[]", userStatus: HttpStatusCode.Unauthorized);

        var identity = await RunCreatingTicketAsync(handler);

        Assert.Empty(identity.Claims);
        Assert.Equal([UserEndpoint], handler.RequestedUris);
    }

    private static async Task<ClaimsIdentity> RunCreatingTicketAsync(StubHandler handler)
    {
        var options = new OAuthOptions { UserInformationEndpoint = UserEndpoint };
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
        var identity = new ClaimsIdentity("GitHub");
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using var backchannel = new HttpClient(handler);
        using var tokens = JsonDocument.Parse("""{"access_token":"test-token"}""");
        using var emptyUser = JsonDocument.Parse("{}");
        var context = new OAuthCreatingTicketContext(new ClaimsPrincipal(identity), new AuthenticationProperties(), new DefaultHttpContext { RequestServices = services }, Scheme(), options, backchannel, OAuthTokenResponse.Success(tokens), emptyUser.RootElement);

        await GitHubAuthenticationEvents.CreatingTicketAsync(context);

        return identity;
    }

    private static AuthenticationScheme Scheme() => new(GitHubAuthenticationEvents.SchemeName, GitHubAuthenticationEvents.SchemeName, typeof(OAuthHandler<OAuthOptions>));

    private WebApplicationFactory<Program> ConfigureFactory(string? clientId, string? clientSecret)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:GitHub:ClientId", clientId ?? string.Empty);
            builder.UseSetting("Authentication:GitHub:ClientSecret", clientSecret ?? string.Empty);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase($"{nameof(GitHubAuthenticationTests)}-{clientId}-{clientSecret}"); options.UseOpenIddict(); });
            });
        });

    private sealed class StubHandler(string userJson, string emailsJson, HttpStatusCode userStatus = HttpStatusCode.OK, HttpStatusCode emailsStatus = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<string> RequestedUris { get; } = [];
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!.ToString());
            Authorizations.Add(request.Headers.Authorization?.ToString());
            var isEmails = request.RequestUri!.ToString() == EmailsEndpoint;
            return Task.FromResult(new HttpResponseMessage(isEmails ? emailsStatus : userStatus) { Content = new StringContent(isEmails ? emailsJson : userJson, Encoding.UTF8, "application/json") });
        }
    }
}
