using System.Security.Claims;
using PocketLedger.Contracts;
using PocketLedger.Security;

// Opens the caller's data key for exactly one request. The headers are removed before anything else sees them, the key is bound
// to the access token's user and session, and the scope zeroes it when the request ends.
public sealed class UserSessionKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        var sessionKey = headers[UserSessionKeyProtocol.SessionKeyHeader].ToString();
        var wrappedKey = headers[UserSessionKeyProtocol.WrappedKeyHeader].ToString();
        headers.Remove(UserSessionKeyProtocol.SessionKeyHeader);
        headers.Remove(UserSessionKeyProtocol.WrappedKeyHeader);
        if (sessionKey.Length == 0 && wrappedKey.Length == 0)
        {
            await next(context);
            return;
        }

        var user = context.User;
        var key = user.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? UserSessionKeyProtocol.TryUnwrap(sessionKey, wrappedKey, userId, user.FindFirstValue(UserSessionKeyProtocol.SessionIdClaim))
            : null;
        if (key is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new ApiError("data_key_invalid", "The session's data key is invalid. Sign in again."));
            return;
        }

        using (UserKeyContext.Enter(key))
        {
            await next(context);
        }
    }
}
