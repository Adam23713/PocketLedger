using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

/// <summary>
/// Safety net behind the admin actions: periodically re-sends the complete list of blocked and deleted users
/// from the Identity database (the source of truth) to the API and the web app, so a push that failed or was
/// lost can never leave a blocked user with working tokens or sessions for longer than one interval.
/// </summary>
public sealed class UserAccessBlockSynchronizer(IServiceProvider services, IUserAccessPropagation propagation, ILogger<UserAccessBlockSynchronizer> logger) : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var dbContext = scope.ServiceProvider.GetRequiredService<PocketLedger.Data.IdentityDbContext>();
                var blockedUserIds = await users.Users.AsNoTracking().Where(user => user.IsAccessBlocked).Select(user => user.Id)
                    .Union(dbContext.DeletedUserTombstones.AsNoTracking().Select(item => item.UserId)).ToArrayAsync(stoppingToken);
                await propagation.SynchronizeAsync(blockedUserIds, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Blocked user synchronization to the API and web app failed; it is retried in {Interval}.", Interval);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }
}
