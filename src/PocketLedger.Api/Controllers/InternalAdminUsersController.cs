using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Security;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Api.Controllers;

[ApiController, AllowAnonymous, ApiExplorerSettings(IgnoreApi = true), Route("api/v1/internal-admin/users")]
public sealed class InternalAdminUsersController(IUserDataDeletionService userDataDeletion, IBlockedUserAdministration blockedUsers, IConfiguration configuration) : ControllerBase
{
    [HttpPut("{userId:guid}/access-block")]
    public async Task<IActionResult> Block(Guid userId, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await blockedUsers.BlockAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}/access-block")]
    public async Task<IActionResult> Unblock(Guid userId, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await blockedUsers.UnblockAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPut("access-blocks")]
    public async Task<IActionResult> Synchronize([FromBody] Guid[] userIds, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        await blockedUsers.SynchronizeAsync(userIds, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        if (!InternalAdminAuthentication.IsAuthorized(Request, configuration)) return Unauthorized();

        // Keep the user's access tokens rejected permanently, so nothing can be written for the deleted user afterwards.
        await blockedUsers.BlockAsync(userId, cancellationToken);
        await userDataDeletion.DeleteAsync(userId, cancellationToken);
        return NoContent();
    }
}
