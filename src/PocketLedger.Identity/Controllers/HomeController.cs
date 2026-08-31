using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PocketLedger.Controllers;

public sealed class HomeController(IConfiguration configuration) : Controller
{
    [AllowAnonymous]
    public IActionResult Index() => User.Identity?.IsAuthenticated == true
        ? Redirect(configuration["OpenIddict:WebBaseUrl"] ?? "https://app.localhost")
        : RedirectToAction("Login", "Account");
}
