using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using PocketLedger.Models.Entities;
using PocketLedger.Security;
using PocketLedger.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PocketLedger.Controllers;

public sealed class AuthorizationController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, IPendingSignInKeys pendingKeys, ISessionKeyHandoff sessionKeys) : Controller
{
    [AllowAnonymous, HttpGet("~/connect/authorize"), HttpPost("~/connect/authorize"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> AuthorizeEndpoint()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        var cookieResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!cookieResult.Succeeded)
        {
            var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
            return RedirectToAction("Login", "Account", new { returnUrl });
        }
        var user = await userManager.GetUserAsync(cookieResult.Principal!);
        if (user is null) return Challenge(IdentityConstants.ApplicationScheme);
        if (user.IsAccessBlocked) return RedirectToAction("AccessBlocked", "Account");
        // Every client session needs the user's data key, which only a sign-in that just used the password can provide.
        // A still-valid Identity cookie alone is not enough, so the user re-enters the password (or data password) first.
        using var dataKey = pendingKeys.TryOpen(HttpContext, user.Id);
        if (dataKey is null) return RedirectToAction("UnlockData", "Account", new { returnUrl = Request.PathBase + Request.Path + Request.QueryString });
        var (handoffId, sessionId) = sessionKeys.Prepare(dataKey);
        pendingKeys.Clear(HttpContext);
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, user.Id.ToString());
        principal.SetClaim(Claims.Name, user.UserName);
        principal.SetClaim(UserSessionKeyProtocol.SessionIdClaim, sessionId);
        principal.SetClaim(UserSessionKeyProtocol.HandoffClaim, handoffId);
        principal.SetScopes(request.GetScopes());
        principal.SetResources("pocketledger-api");
        SetDestinations(principal);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [AllowAnonymous, HttpPost("~/connect/token"), IgnoreAntiforgeryToken]
    public async Task<IActionResult> TokenEndpoint()
    {
        var request = HttpContext.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType()) return BadRequest();
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(Claims.Subject);
        var user = subject is null ? null : await userManager.FindByIdAsync(subject);
        if (user is null || user.IsAccessBlocked || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        // Tokens issued before per-user data keys, or without a data session, cannot be used any more.
        var tokenPrincipal = result.Principal!;
        var sessionId = tokenPrincipal.GetClaim(UserSessionKeyProtocol.SessionIdClaim);
        if (string.IsNullOrEmpty(sessionId)) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var properties = new AuthenticationProperties();
        if (request.IsAuthorizationCodeGrantType())
        {
            var handoffId = tokenPrincipal.GetClaim(UserSessionKeyProtocol.HandoffClaim);
            var material = string.IsNullOrEmpty(handoffId) ? null : sessionKeys.Take(handoffId, user.Id, sessionId);
            if (material is null) return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            properties.Parameters[UserSessionKeyProtocol.SessionKeyParameter] = material.SessionKey;
            properties.Parameters[UserSessionKeyProtocol.WrappedKeyParameter] = material.WrappedKey;
        }
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, user.Id.ToString());
        principal.SetClaim(Claims.Name, user.UserName);
        principal.SetClaim(UserSessionKeyProtocol.SessionIdClaim, sessionId);
        principal.SetScopes(tokenPrincipal.GetScopes());
        principal.SetResources("pocketledger-api");
        SetDestinations(principal);
        return SignIn(principal, properties, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static void SetDestinations(ClaimsPrincipal principal)
    {
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                Claims.Name or Claims.Subject or BootstrapAdministratorAuthorization.ClaimType => [Destinations.AccessToken, Destinations.IdentityToken],
                // Stays inside the authorization code, which only Identity can read.
                UserSessionKeyProtocol.HandoffClaim => [],
                _ => [Destinations.AccessToken]
            });
        }
    }

    [Authorize, HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    public async Task<IActionResult> LogoutEndpoint()
    {
        await signInManager.SignOutAsync();
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
