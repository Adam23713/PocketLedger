using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using PocketLedger.Models.Entities;

namespace PocketLedger.Middleware;

public sealed class MandatoryTwoFactorMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> AllowedAccountActions = new(StringComparer.Ordinal)
    {
        "SetupAuthenticator",
        "RecoveryCodes",
        "CreateDataPassword",
        "UnlockData",
        "Logout"
    };

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsAllowedDuringTwoFactorSetup(context))
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

    private static bool IsAllowedDuringTwoFactorSetup(HttpContext context)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return action is { ControllerName: "Account", ActionName: var actionName }
            && AllowedAccountActions.Contains(actionName);
    }
}
