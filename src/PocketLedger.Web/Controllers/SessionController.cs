using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace PocketLedger.Controllers;

public sealed class SessionController(IConfiguration configuration) : Controller
{
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
    public IActionResult LoginFailed() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : View();

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = configuration["Landing:BaseUrl"] ?? "https://pocketledger.dev" }, "BffCookie", "oidc");
}
