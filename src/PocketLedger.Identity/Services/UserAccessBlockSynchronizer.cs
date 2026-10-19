using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Services;

public sealed class UserAccessBlockSynchronizer(IServiceProvider services, ILogger<UserAccessBlockSynchronizer> logger) : BackgroundService
{
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
                await scope.ServiceProvider.GetRequiredService<IUserAccessBlockStore>().InitializeIfMissingAsync(blockedUserIds, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Blocked user state synchronization failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
