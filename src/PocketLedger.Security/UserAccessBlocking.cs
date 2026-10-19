using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PocketLedger.Security;

/// <summary>
/// Authoritative per-request lookup of whether a user is blocked. Implementations must read durable storage
/// (a database), never a cache that can silently lose entries.
/// </summary>
public interface IUserAccessBlockStore
{
    Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default);
}

public enum UserAccessBlockState
{
    Active,
    Blocked,
    Unavailable
}

public static class UserAccessBlockingRegistration
{
    public static IApplicationBuilder UseUserAccessBlocking(this IApplicationBuilder app, string? blockedPath = null)
        => blockedPath is null ? app.UseMiddleware<UserAccessBlockMiddleware>() : app.UseMiddleware<UserAccessBlockMiddleware>(blockedPath);
}

public sealed class UserAccessBlockMiddleware(RequestDelegate next, string? blockedPath = null)
{
    public async Task InvokeAsync(HttpContext context, IUserAccessBlockStore store)
    {
        if (TryGetUserId(context.User, out var userId))
        {
            var state = await store.IsBlockedAsync(userId, context.RequestAborted);
            if (state == UserAccessBlockState.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("User access validation is temporarily unavailable.", context.RequestAborted);
                return;
            }
            if (state == UserAccessBlockState.Blocked && (blockedPath is null || !context.Request.Path.Equals(blockedPath, StringComparison.OrdinalIgnoreCase)))
            {
                if (blockedPath is not null)
                {
                    context.Response.Redirect($"{context.Request.PathBase}{blockedPath}");
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { Error = "Access to this account is currently blocked and under review." }, context.RequestAborted);
                return;
            }
        }
        await next(context);
    }

    private static bool TryGetUserId(System.Security.Claims.ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
