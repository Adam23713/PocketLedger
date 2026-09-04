using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using PocketLedger.Models.Entities;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace PocketLedger.Controllers;

public sealed class AuthorizationController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager) : Controller
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
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, user.Id.ToString());
        principal.SetClaim(Claims.Name, user.UserName);
        principal.SetScopes(request.GetScopes());
        principal.SetResources("pocketledger-api");
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                Claims.Name or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
                _ => [Destinations.AccessToken]
            });
        }
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
        if (user is null || !await signInManager.CanSignInAsync(user))
        {
            return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        principal.SetClaim(Claims.Subject, user.Id.ToString());
        principal.SetClaim(Claims.Name, user.UserName);
        principal.SetScopes(result.Principal!.GetScopes());
        principal.SetResources("pocketledger-api");
        foreach (var claim in principal.Claims)
        {
            claim.SetDestinations(claim.Type switch
            {
                Claims.Name or Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
                _ => [Destinations.AccessToken]
            });
        }
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [Authorize, HttpGet("~/connect/logout"), HttpPost("~/connect/logout")]
    public async Task<IActionResult> LogoutEndpoint()
    {
        await signInManager.SignOutAsync();
        return SignOut(new AuthenticationProperties { RedirectUri = "/" }, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
