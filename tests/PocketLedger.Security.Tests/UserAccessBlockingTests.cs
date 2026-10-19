using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PocketLedger.Security.Tests;

public sealed class UserAccessBlockingTests
{
    [Fact]
    public async Task BlockedWebRequest_RedirectsToBlockedPage()
    {
        var context = CreateAuthenticatedContext("/Transactions");
        var middleware = new UserAccessBlockMiddleware(_ => Task.CompletedTask, "/Session/AccessBlocked");

        await middleware.InvokeAsync(context, new BlockedStore());

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/Session/AccessBlocked", context.Response.Headers.Location);
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string path)
    {
        var userId = Guid.NewGuid();
        return new DefaultHttpContext
        {
            Request = { Path = path },
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test"))
        };
    }

    private sealed class BlockedStore : IUserAccessBlockStore
    {
        public Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(UserAccessBlockState.Blocked);
        public Task SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InitializeIfMissingAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
