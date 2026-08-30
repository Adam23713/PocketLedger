using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PocketLedger.Controllers;

public sealed class HomeController : Controller
{
    [AllowAnonymous]
    public IActionResult Index() => View();
}
