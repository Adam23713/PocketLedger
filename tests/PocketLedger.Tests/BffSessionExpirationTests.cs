using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Web.Authentication;

namespace PocketLedger.Tests;

public class BffSessionExpirationTests
{
    [Fact]
    public async Task AccessTokenHandler_TreatsInvalidGrantAsExpiredBffSession()
    {
        using var handler = CreateAccessTokenHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<BffSessionExpiredException>(() => client.GetAsync("https://api.test/accounts"));
    }

    [Fact]
    public async Task AccessTokenHandler_DoesNotHideOtherTokenEndpointErrorsAsExpiredSessions()
    {
        using var handler = CreateAccessTokenHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://api.test/accounts"));
    }

    [Fact]
    public async Task Middleware_SignsOutBffSessionAndChallengesOidc()
    {
        var authentication = new StubAuthenticationService(AuthenticateResult.NoResult());
        var httpContext = CreateHttpContext(authentication);
        httpContext.Request.Method = HttpMethods.Get;
        httpContext.Request.Path = "/Statistics";
        httpContext.Request.QueryString = new QueryString("?month=8");

        await new BffSessionExpiredMiddleware(_ => throw new BffSessionExpiredException()).InvokeAsync(httpContext);

        Assert.Equal("BffCookie", authentication.SignedOutScheme);
        Assert.Equal("oidc", authentication.ChallengedScheme);
        Assert.Equal("/Statistics?month=8", authentication.ChallengeProperties?.RedirectUri);
    }

    private static AccessTokenHandler CreateAccessTokenHandler(HttpStatusCode statusCode, string tokenResponse)
    {
        var properties = new AuthenticationProperties();
        properties.StoreTokens([new AuthenticationToken { Name = "refresh_token", Value = "expired-refresh-token" }]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-id")], "BffCookie"));
        var authentication = new StubAuthenticationService(AuthenticateResult.Success(new AuthenticationTicket(principal, properties, "BffCookie")));
        var context = CreateHttpContext(authentication);
        var tokenClient = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
        })) { BaseAddress = new Uri("https://identity.test/") };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:ClientId"] = "pocketledger-web",
            ["Identity:ClientSecret"] = "secret",
            ["Identity:TokenEndpoint"] = "connect/token"
        }).Build();
        return new AccessTokenHandler(new HttpContextAccessor { HttpContext = context }, new StubHttpClientFactory(tokenClient), configuration)
        {
            InnerHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };
    }

    private static DefaultHttpContext CreateHttpContext(IAuthenticationService authentication)
    {
        var services = new ServiceCollection().AddSingleton(authentication).BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services };
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class StubAuthenticationService(AuthenticateResult result) : IAuthenticationService
    {
        public AuthenticationProperties? ChallengeProperties { get; private set; }
        public string? ChallengedScheme { get; private set; }
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(result);
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ChallengedScheme = scheme;
            ChallengeProperties = properties;
            return Task.CompletedTask;
        }
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }
}
