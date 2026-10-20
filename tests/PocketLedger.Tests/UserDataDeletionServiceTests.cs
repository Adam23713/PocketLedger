using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public sealed class UserDataDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_RemovesOnlySelectedUsersData()
    {
        var deletedUserId = Guid.NewGuid();
        var retainedUserId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;

        await using (var setup = new PocketLedgerDbContext(options, new TestCurrentUser(deletedUserId)))
        {
            await setup.Database.EnsureCreatedAsync();
            setup.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = deletedUserId, Name = "Deleted", Type = AccountType.BankAccount, Currency = "HUF" });
            var parentCategoryId = Guid.NewGuid();
            setup.Categories.AddRange(
                new Category { Id = parentCategoryId, OwnerId = deletedUserId, Name = "Parent", Type = CategoryType.Expense },
                new Category { Id = Guid.NewGuid(), OwnerId = deletedUserId, Name = "Child", Type = CategoryType.Expense, ParentCategoryId = parentCategoryId });
            setup.UserPreferences.Add(new UserPreference { UserId = deletedUserId });
            await setup.SaveChangesAsync();
        }
        await using (var setup = new PocketLedgerDbContext(options, new TestCurrentUser(retainedUserId)))
        {
            setup.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = retainedUserId, Name = "Retained", Type = AccountType.BankAccount, Currency = "EUR" });
            setup.UserPreferences.Add(new UserPreference { UserId = retainedUserId });
            await setup.SaveChangesAsync();
        }

        await using (var deletion = new PocketLedgerDbContext(options))
            await new UserDataDeletionService(deletion).DeleteAsync(deletedUserId);

        await using var verification = new PocketLedgerDbContext(options);
        Assert.False(await verification.Accounts.IgnoreQueryFilters().AnyAsync(item => item.OwnerId == deletedUserId));
        Assert.False(await verification.UserPreferences.IgnoreQueryFilters().AnyAsync(item => item.UserId == deletedUserId));
        Assert.False(await verification.Categories.IgnoreQueryFilters().AnyAsync(item => item.OwnerId == deletedUserId));
        Assert.True(await verification.Accounts.IgnoreQueryFilters().AnyAsync(item => item.OwnerId == retainedUserId));
        Assert.True(await verification.UserPreferences.IgnoreQueryFilters().AnyAsync(item => item.UserId == retainedUserId));
    }

    private sealed record TestCurrentUser(Guid UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
