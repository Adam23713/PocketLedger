using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

/// <summary>
/// Validates the Identity sign-in cookie against the database on every request, so a blocked or deleted user
/// loses the Identity session immediately instead of after the security-stamp validation interval.
/// </summary>
public static class BlockedUserCookieValidation
{
    public const string BlockedPath = "/Account/AccessBlocked";
    private const string BlockedItemKey = "PocketLedger.Identity.AccessBlocked";

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var userId = context.Principal is null ? null : userManager.GetUserId(context.Principal);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null || user.IsAccessBlocked)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
            if (user is not null) context.HttpContext.Items[BlockedItemKey] = true;
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    public static IApplicationBuilder UseBlockedUserRedirect(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Items.ContainsKey(BlockedItemKey) && !context.Request.Path.Equals(BlockedPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect($"{context.Request.PathBase}{BlockedPath}");
            return;
        }
        await next();
    });
}
