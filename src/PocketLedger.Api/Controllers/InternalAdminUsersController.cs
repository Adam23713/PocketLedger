using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Security;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true), Route("api/v1/internal-admin/users")]
public sealed class InternalAdminUsersController(IUserDataDeletionService userDataDeletion, IConfiguration configuration) : ControllerBase
{
    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await userDataDeletion.DeleteAsync(userId, cancellationToken);
        return NoContent();
    }
}
