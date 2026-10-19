using Microsoft.AspNetCore.Authentication;

namespace PocketLedger.Web.Authentication;

/// <summary>
/// Runs after authentication. When the browser presented a session that an administrator revoked, the cookie is
/// cleared and the request is sent to the "access blocked" page instead of reaching any application endpoint.
/// </summary>
public sealed class RevokedSessionMiddleware(RequestDelegate next)
{
    public const string BlockedPath = "/Session/AccessBlocked";
    private const string StatusPath = "/Session/Status";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items.ContainsKey(DatabaseTicketStore.RevokedSessionItemKey))
        {
            await context.SignOutAsync("BffCookie");
            if (!context.Request.Path.Equals(BlockedPath, StringComparison.OrdinalIgnoreCase) && !context.Request.Path.Equals(StatusPath, StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Redirect($"{context.Request.PathBase}{BlockedPath}");
                return;
            }
        }
        await next(context);
    }
}
