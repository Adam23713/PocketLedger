using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Services;

namespace PocketLedger.Controllers;

// Read by the landing page so its banner reflects the registration settings managed on the admin dashboard.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RegistrationController(IRegistrationSettingsService registrationSettings) : Controller
{
    [AllowAnonymous, HttpGet]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var status = await registrationSettings.GetStatusAsync(cancellationToken);
        return Json(new { isOpen = status.IsOpen, acceptsRegistrations = status.AcceptsRegistrations, maximumUserCount = status.MaximumUserCount, remainingSlots = status.RemainingSlots });
    }
}
