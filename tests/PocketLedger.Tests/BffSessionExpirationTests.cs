using System.Collections.Concurrent;
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
    public async Task AccessTokenHandler_RefreshesOnlyOnceForConcurrentRequestsInTheSameSession()
    {
        var coordinator = new SessionRefreshCoordinator();
        var ticketReader = new StubSessionTicketReader();
        ticketReader.Set("session-1", CreateTicket("expired-refresh-token"));
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondAuthenticated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCalls = 0;
        using var tokenClient = new HttpClient(new AsyncStubHttpMessageHandler(async (_, cancellationToken) =>
        {
            Interlocked.Increment(ref refreshCalls);
            refreshStarted.TrySetResult();
            await releaseRefresh.Task.WaitAsync(cancellationToken);
            return TokenResponse("new-access-token", "rotated-refresh-token");
        }))
        {
            BaseAddress = new Uri("https://identity.test/")
        };

        using var firstHandler = CreateAccessTokenHandler(HttpStatusCode.OK, "{}", "session-1", ticketReader, coordinator, tokenClient);
        using var secondHandler = CreateAccessTokenHandler(HttpStatusCode.OK, "{}", "session-1", ticketReader, coordinator, tokenClient, () => secondAuthenticated.TrySetResult());
        using var firstClient = new HttpClient(firstHandler);
        using var secondClient = new HttpClient(secondHandler);

        var firstRequest = firstClient.GetAsync("https://api.test/accounts");
        await refreshStarted.Task;
        var secondRequest = secondClient.GetAsync("https://api.test/accounts");
        await secondAuthenticated.Task;
        releaseRefresh.TrySetResult();
        await Task.WhenAll(firstRequest, secondRequest);

        Assert.Equal(1, refreshCalls);
        Assert.Equal("rotated-refresh-token", ticketReader.Get("session-1")!.Properties.GetTokenValue("refresh_token"));
        Assert.Equal(0, coordinator.TrackedSessionCount);
    }

    [Fact]
    public async Task SessionRefreshCoordinator_DifferentSessionsDoNotBlockEachOther()
    {
        var coordinator = new SessionRefreshCoordinator();
        using var firstSession = await coordinator.AcquireAsync("session-1", CancellationToken.None);
        using var secondSession = await coordinator.AcquireAsync("session-2", CancellationToken.None);

        Assert.Equal(2, coordinator.TrackedSessionCount);
    }

    [Fact]
    public async Task SessionRefreshCoordinator_ReleasesStateAfterCancellationAndFailure()
    {
        var coordinator = new SessionRefreshCoordinator();
        using var first = await coordinator.AcquireAsync("session", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var waiting = coordinator.AcquireAsync("session", cancellation.Token).AsTask();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        first.Dispose();
        Assert.Equal(0, coordinator.TrackedSessionCount);

        using var reacquired = await coordinator.AcquireAsync("session", CancellationToken.None);
        Assert.Equal(1, coordinator.TrackedSessionCount);
    }

    [Fact]
    public async Task AccessTokenHandler_TreatsInvalidGrantAsExpiredBffSession()
    {
        using var handler = CreateAccessTokenHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", "session");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<BffSessionExpiredException>(() => client.GetAsync("https://api.test/accounts"));
    }

    [Fact]
    public async Task AccessTokenHandler_DoesNotHideOtherTokenEndpointErrorsAsExpiredSessions()
    {
        var coordinator = new SessionRefreshCoordinator();
        using var handler = CreateAccessTokenHandler(HttpStatusCode.BadRequest, "{\"error\":\"invalid_client\"}", "session", coordinator: coordinator);
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://api.test/accounts"));
        Assert.Equal(0, coordinator.TrackedSessionCount);
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

    private static AccessTokenHandler CreateAccessTokenHandler(HttpStatusCode statusCode, string tokenResponse, string sessionKey, StubSessionTicketReader? ticketReader = null, SessionRefreshCoordinator? coordinator = null, HttpClient? tokenClient = null, Action? onAuthenticate = null)
    {
        var ticket = CreateTicket("expired-refresh-token", sessionKey);
        ticketReader ??= new StubSessionTicketReader();
        ticketReader.Set(sessionKey, ticket);
        var authentication = new StubAuthenticationService(AuthenticateResult.Success(ticket), signedInTicket => ticketReader.Set(sessionKey, signedInTicket), onAuthenticate);
        var context = CreateHttpContext(authentication);
        tokenClient ??= new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(tokenResponse, Encoding.UTF8, "application/json")
        }))
        {
            BaseAddress = new Uri("https://identity.test/")
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Identity:ClientId"] = "pocketledger-web",
            ["Identity:ClientSecret"] = "secret",
            ["Identity:TokenEndpoint"] = "connect/token"
        }).Build();
        return new AccessTokenHandler(new HttpContextAccessor { HttpContext = context }, new StubHttpClientFactory(tokenClient), configuration, ticketReader, coordinator ?? new SessionRefreshCoordinator())
        {
            InnerHandler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };
    }

    private static AuthenticationTicket CreateTicket(string refreshToken, string sessionKey = "session")
    {
        var properties = new AuthenticationProperties();
        properties.Items[DatabaseTicketStore.SessionKeyProperty] = sessionKey;
        properties.StoreTokens([new AuthenticationToken { Name = "refresh_token", Value = refreshToken }]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-id")], "BffCookie"));
        return new AuthenticationTicket(principal, properties, "BffCookie");
    }

    private static HttpResponseMessage TokenResponse(string accessToken, string refreshToken) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($$"""{"access_token":"{{accessToken}}","refresh_token":"{{refreshToken}}","expires_in":3600}""", Encoding.UTF8, "application/json")
    };

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

    private sealed class StubAuthenticationService(AuthenticateResult result, Action<AuthenticationTicket>? onSignIn = null, Action? onAuthenticate = null) : IAuthenticationService
    {
        public AuthenticationProperties? ChallengeProperties { get; private set; }
        public string? ChallengedScheme { get; private set; }
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            onAuthenticate?.Invoke();
            return Task.FromResult(result);
        }
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            ChallengedScheme = scheme;
            ChallengeProperties = properties;
            return Task.CompletedTask;
        }
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties)
        {
            onSignIn?.Invoke(new AuthenticationTicket(principal, properties ?? new AuthenticationProperties(), scheme ?? "BffCookie"));
            return Task.CompletedTask;
        }
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }

    private sealed class AsyncStubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }

    private sealed class StubSessionTicketReader : ISessionTicketReader
    {
        private readonly ConcurrentDictionary<string, AuthenticationTicket> tickets = new();

        public AuthenticationTicket? Get(string key) => tickets.GetValueOrDefault(key);
        public Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken) => Task.FromResult(Get(key));
        public void Set(string key, AuthenticationTicket ticket) => tickets[key] = ticket;
    }
}
