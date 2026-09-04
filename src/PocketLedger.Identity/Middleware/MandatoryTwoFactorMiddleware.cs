using Microsoft.AspNetCore.Identity;
using PocketLedger.Models.Entities;

namespace PocketLedger.Middleware;

public sealed class MandatoryTwoFactorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true && !context.Request.Path.StartsWithSegments("/Account") && !context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js") && !context.Request.Path.StartsWithSegments("/lib") && !context.Request.Path.StartsWithSegments("/images"))
        {
            var user = await userManager.GetUserAsync(context.User);
            if (user is null || !user.TwoFactorEnabled || !user.AuthenticatorSetupComplete)
            {
                context.Response.Redirect("/Account/SetupAuthenticator");
                return;
            }
        }
        await next(context);
    }
}
