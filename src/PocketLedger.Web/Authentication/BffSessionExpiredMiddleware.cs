using Microsoft.AspNetCore.Authentication;

namespace PocketLedger.Web.Authentication;

public sealed class BffSessionExpiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (BffSessionExpiredException)
        {
            if (context.Response.HasStarted) throw;

            await context.SignOutAsync("BffCookie");
            var request = context.Request;
            var redirectUri = HttpMethods.IsGet(request.Method) ? $"{request.PathBase}{request.Path}{request.QueryString}" : "/";
            await context.ChallengeAsync("oidc", new AuthenticationProperties { RedirectUri = redirectUri });
        }
    }
}
