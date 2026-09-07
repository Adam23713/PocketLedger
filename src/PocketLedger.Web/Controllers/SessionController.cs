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
    public IActionResult Login(string? returnUrl = null) => Challenge(new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" }, "oidc");

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = configuration["Landing:BaseUrl"] ?? "https://pocketledger.dev" }, "BffCookie", "oidc");
}
