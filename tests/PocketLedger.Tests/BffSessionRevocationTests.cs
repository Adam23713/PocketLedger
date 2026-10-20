using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Web.Authentication;
using PocketLedger.Web.Data;

namespace PocketLedger.Tests;

public sealed class BffSessionRevocationTests
{
    [Fact]
    public async Task RevokeUserSessions_TerminatesEverySessionOfTheBlockedUserOnly()
    {
        var (store, accessor, services) = CreateStore();
        var blockedUser = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var firstDevice = await store.StoreAsync(CreateTicket(blockedUser));
        var secondDevice = await store.StoreAsync(CreateTicket(blockedUser));
        var unaffected = await store.StoreAsync(CreateTicket(otherUser));

        var revoked = await store.RevokeUserSessionsAsync([blockedUser]);

        Assert.Equal(2, revoked);
        Assert.Null(await store.RetrieveAsync(firstDevice));
        Assert.True(accessor.HttpContext!.Items.ContainsKey(DatabaseTicketStore.RevokedSessionItemKey));
        Assert.Null(await store.RetrieveAsync(secondDevice));
        Assert.NotNull(await store.RetrieveAsync(unaffected));
        await using var scope = services.CreateAsyncScope();
        var sessions = await scope.ServiceProvider.GetRequiredService<WebDbContext>().Sessions.Where(item => item.UserId == blockedUser).ToListAsync();
        Assert.All(sessions, session => Assert.Empty(session.ProtectedTicket));
    }

    [Fact]
    public async Task RenewAsync_CannotResurrectARevokedSession()
    {
        var (store, _, _) = CreateStore();
        var userId = Guid.NewGuid();
        var key = await store.StoreAsync(CreateTicket(userId));
        await store.RevokeUserSessionsAsync([userId]);

        await store.RenewAsync(key, CreateTicket(userId));

        Assert.Null(await store.RetrieveAsync(key));
    }

    [Fact]
    public async Task RevokedSessionMiddleware_SignsOutAndRedirectsToBlockedPage()
    {
        var authentication = new RecordingAuthenticationService();
        var context = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton<IAuthenticationService>(authentication).BuildServiceProvider() };
        context.Request.Path = "/Transactions";
        context.Request.PathBase = "//attacker.example";
        context.Items[DatabaseTicketStore.RevokedSessionItemKey] = true;
        var nextCalled = false;

        await new RevokedSessionMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal("BffCookie", authentication.SignedOutScheme);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(RevokedSessionMiddleware.BlockedPath, context.Response.Headers.Location);
    }

    private static (DatabaseTicketStore Store, HttpContextAccessor Accessor, ServiceProvider Services) CreateStore()
    {
        // The name must be fixed outside the options callback, which runs for every new scope.
        var databaseName = $"{nameof(BffSessionRevocationTests)}-{Guid.NewGuid():N}";
        var services = new ServiceCollection()
            .AddDbContext<WebDbContext>(options => options.UseInMemoryDatabase(databaseName))
            .BuildServiceProvider();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return (new DatabaseTicketStore(services.GetRequiredService<IServiceScopeFactory>(), new EphemeralDataProtectionProvider(), accessor), accessor, services);
    }

    private static AuthenticationTicket CreateTicket(Guid userId)
    {
        var properties = new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) };
        properties.StoreTokens([new AuthenticationToken { Name = "refresh_token", Value = "refresh" }]);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "BffCookie"));
        return new AuthenticationTicket(principal, properties, "BffCookie");
    }

    private sealed class RecordingAuthenticationService : IAuthenticationService
    {
        public string? SignedOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => Task.FromResult(AuthenticateResult.NoResult());
        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOutScheme = scheme;
            return Task.CompletedTask;
        }
    }
}
