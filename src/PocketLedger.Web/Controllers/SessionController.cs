using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PocketLedger.Controllers;

public sealed class SessionController : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => Challenge(new AuthenticationProperties { RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/" }, "oidc");

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public IActionResult Logout() => SignOut(new AuthenticationProperties { RedirectUri = "/" }, "BffCookie", "oidc");
}
