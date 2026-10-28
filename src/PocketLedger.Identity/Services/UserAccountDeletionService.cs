using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public interface IUserAccountDeletionService
{
    Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public sealed class UserAccountDeletionService(UserManager<ApplicationUser> userManager, IdentityDbContext dbContext, IUserAccessPropagation accessPropagation,
    IAdminUserDataClient userDataClient, TimeProvider timeProvider) : IUserAccountDeletionService
{
    public async Task DeleteAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (user.IsBootstrapAdministrator) throw new InvalidOperationException("The bootstrap administrator cannot be deleted.");

        user.IsAccessBlocked = true;
        var blockResult = await userManager.UpdateSecurityStampAsync(user);
        if (!blockResult.Succeeded) throw new InvalidOperationException("The user could not be blocked before deletion.");
        await RevokeIdentitySessionsAsync(user.Id, cancellationToken);

        // Deletion only continues once the API rejects the user's tokens and every web session is gone.
        await accessPropagation.BlockAsync(user.Id, cancellationToken);
        await userDataClient.DeleteAsync(user.Id, cancellationToken);

        await using var transaction = dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await DeleteIdentityHistoryAsync(user.Id, cancellationToken);
        dbContext.DeletedUserTombstones.Add(new DeletedUserTombstone { UserId = user.Id, DeletedAtUtc = timeProvider.GetUtcNow() });
        var deleteResult = await userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded) throw new InvalidOperationException("The user identity could not be deleted.");
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task RevokeIdentitySessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject && item.Status != OpenIddictConstants.Statuses.Revoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
            await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject && item.Status != OpenIddictConstants.Statuses.Revoked)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
            return;
        }

        foreach (var token in await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject).ToListAsync(cancellationToken))
            token.Status = OpenIddictConstants.Statuses.Revoked;
        foreach (var authorization in await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject).ToListAsync(cancellationToken))
            authorization.Status = OpenIddictConstants.Statuses.Revoked;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteIdentityHistoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var subject = userId.ToString();
        if (dbContext.Database.IsRelational())
        {
            await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject).ExecuteDeleteAsync(cancellationToken);
            await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject).ExecuteDeleteAsync(cancellationToken);
            await dbContext.AuthenticationAuditEvents.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await dbContext.UserDataKeys.Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        dbContext.RemoveRange(await dbContext.Set<OpenIddictEntityFrameworkCoreToken>().Where(item => item.Subject == subject).ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.Set<OpenIddictEntityFrameworkCoreAuthorization>().Where(item => item.Subject == subject).ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.AuthenticationAuditEvents.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        dbContext.RemoveRange(await dbContext.UserDataKeys.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
    }
}
