using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public sealed class BlockedUserServiceTests
{
    [Fact]
    public async Task SynchronizeAsync_ConcurrentBlockOfTheSameUser_ConvergesWithoutDuplicateKeyFailure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"pocketledger-blocked-users-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";
        var userId = Guid.NewGuid();
        try
        {
            var setupOptions = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connectionString).Options;
            await using (var setup = new PocketLedgerDbContext(setupOptions)) await setup.Database.EnsureCreatedAsync();

            var interceptor = new ConcurrentBlockInterceptor(connectionString, userId);
            var synchronizationOptions = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connectionString).AddInterceptors(interceptor).Options;
            await using var synchronizationContext = new PocketLedgerDbContext(synchronizationOptions);
            var service = new BlockedUserService(synchronizationContext, TimeProvider.System);

            await service.SynchronizeAsync([userId]);

            await using var verification = new PocketLedgerDbContext(setupOptions);
            Assert.Equal(userId, (await verification.BlockedUsers.SingleAsync()).UserId);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(databasePath);
        }
    }

    private sealed class ConcurrentBlockInterceptor(string connectionString, Guid userId) : SaveChangesInterceptor
    {
        private int hasInserted;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref hasInserted, 1) == 0)
            {
                var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connectionString).Options;
                await using var concurrentContext = new PocketLedgerDbContext(options);
                concurrentContext.BlockedUsers.Add(new BlockedUser { UserId = userId, BlockedAtUtc = TimeProvider.System.GetUtcNow() });
                await concurrentContext.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
