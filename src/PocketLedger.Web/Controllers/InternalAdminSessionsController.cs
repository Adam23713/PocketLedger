using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Security;
using PocketLedger.Web.Authentication;

namespace PocketLedger.Controllers;

// Service-to-service endpoint called by Identity when an administrator blocks or deletes a user.
[ApiController, AllowAnonymous, IgnoreAntiforgeryToken, Route("internal-admin/users")]
public sealed class InternalAdminSessionsController(DatabaseTicketStore sessions, IConfiguration configuration) : ControllerBase
{
    [HttpPost("{userId:guid}/sessions/revoke")]
    // This service endpoint authenticates with a dedicated secret header and never with browser cookies.
    // codeql[cs/web/missing-token-validation]
    public async Task<IActionResult> RevokeUser(Guid userId, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await sessions.RevokeUserSessionsAsync([userId], cancellationToken);
        return NoContent();
    }

    [HttpPost("sessions/revoke")]
    // This service endpoint authenticates with a dedicated secret header and never with browser cookies.
    // codeql[cs/web/missing-token-validation]
    public async Task<IActionResult> RevokeUsers([FromBody] Guid[] userIds, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await sessions.RevokeUserSessionsAsync(userIds, cancellationToken);
        return NoContent();
    }
}
