using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Web.Api;

namespace PocketLedger.Controllers;

[Authorize]
public sealed class ChangelogController(IPreferencesApiClient preferences) : Controller
{
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Seen(string version, CancellationToken token)
    {
        if (!Changelog.Exists(version)) return BadRequest();
        await preferences.MarkChangelogSeenAsync(version, token);
        return NoContent();
    }
}
