using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public sealed class BlockedUserService(PocketLedgerDbContext dbContext, TimeProvider timeProvider) : IUserAccessBlockStore, IBlockedUserAdministration
{
    // A bulk synchronization never removes rows younger than this. Identity takes its snapshot slightly before it
    // pushes it, so without the grace period a snapshot taken just before an admin block could undo that block.
    internal static readonly TimeSpan SynchronizationGracePeriod = TimeSpan.FromMinutes(2);

    public async Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default)
        => await dbContext.BlockedUsers.AsNoTracking().AnyAsync(item => item.UserId == userId, cancellationToken)
            ? UserAccessBlockState.Blocked
            : UserAccessBlockState.Active;

    public async Task BlockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (await dbContext.BlockedUsers.AnyAsync(item => item.UserId == userId, cancellationToken)) return;
        dbContext.BlockedUsers.Add(new BlockedUser { UserId = userId, BlockedAtUtc = timeProvider.GetUtcNow() });
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request inserted the same user; the block is in place either way.
            dbContext.ChangeTracker.Clear();
            if (!await dbContext.BlockedUsers.AsNoTracking().AnyAsync(item => item.UserId == userId, cancellationToken)) throw;
        }
    }

    public async Task UnblockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.BlockedUsers.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (existing is null) return;
        dbContext.BlockedUsers.Remove(existing);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SynchronizeAsync(IReadOnlyCollection<Guid> blockedUserIds, CancellationToken cancellationToken = default)
    {
        var desired = blockedUserIds.ToHashSet();
        var existing = await dbContext.BlockedUsers.ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var removableBefore = now - SynchronizationGracePeriod;
        foreach (var item in existing.Where(item => !desired.Contains(item.UserId) && item.BlockedAtUtc <= removableBefore))
            dbContext.BlockedUsers.Remove(item);
        foreach (var userId in desired.Except(existing.Select(item => item.UserId)))
            dbContext.BlockedUsers.Add(new BlockedUser { UserId = userId, BlockedAtUtc = now });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
