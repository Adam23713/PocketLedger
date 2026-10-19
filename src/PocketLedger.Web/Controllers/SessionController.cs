using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace PocketLedger.Controllers;

public sealed class SessionController(IConfiguration configuration) : Controller
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> AccessBlocked()
    {
        await HttpContext.SignOutAsync("BffCookie");
        return View();
    }

    [AllowAnonymous, HttpGet, EnableCors("LandingSession")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status() => Json(new { authenticated = User.Identity?.IsAuthenticated == true });

    [AllowAnonymous, HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Login(string? returnUrl = null)
    {
        var destination = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        return User.Identity?.IsAuthenticated == true ? LocalRedirect(destination) : Challenge(new AuthenticationProperties { RedirectUri = destination }, "oidc");
    }

    [AllowAnonymous, HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Register()
    {
        await HttpContext.SignOutAsync("BffCookie");
        var identityBaseUrl = new Uri(configuration["Identity:Authority"] ?? "https://identity.localhost/");
        return Redirect(new Uri(identityBaseUrl, "Account/Register").ToString());
    }

    [AllowAnonymous, HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CompleteSignIn(string? returnUrl = null)
    {
        var destination = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
        await HttpContext.SignOutAsync("BffCookie");
        return Challenge(new AuthenticationProperties { RedirectUri = destination }, "oidc");
    }

    [AllowAnonymous, HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CompleteLogout()
    {
        await HttpContext.SignOutAsync("BffCookie");
        return Redirect(configuration["Landing:BaseUrl"] ?? "https://pocketledger.dev");
    }

    [AllowAnonymous, HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult LoginFailed() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = configuration["Landing:BaseUrl"] ?? "https://pocketledger.dev" }, "BffCookie", "oidc");
}
