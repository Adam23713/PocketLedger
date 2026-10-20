using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PocketLedger.Security.Tests;

public sealed class UserAccessBlockingTests
{
    [Fact]
    public async Task BlockedWebRequest_RedirectsToBlockedPage()
    {
        var context = CreateAuthenticatedContext("/Transactions");
        context.Request.PathBase = "//attacker.example";
        var middleware = new UserAccessBlockMiddleware(_ => Task.CompletedTask, "/Session/AccessBlocked");

        await middleware.InvokeAsync(context, new BlockedStore());

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/Session/AccessBlocked", context.Response.Headers.Location);
    }

    [Theory]
    [InlineData("https://attacker.example/blocked")]
    [InlineData("//attacker.example/blocked")]
    [InlineData("/\\attacker.example/blocked")]
    public void Constructor_RejectsNonLocalBlockedPath(string blockedPath)
    {
        Assert.Throws<ArgumentException>(() => new UserAccessBlockMiddleware(_ => Task.CompletedTask, blockedPath));
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
    }
}
