using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class UserIsolationTests
{
    [Fact]
    public async Task QueryFilters_ReturnOnlyCurrentUsersFinancialData()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var seed = new PocketLedgerDbContext(options))
        {
            seed.Accounts.AddRange(CreateAccount(owner, "Mine"), CreateAccount(other, "Other"));
            seed.Categories.AddRange(CreateCategory(owner, "Mine"), CreateCategory(other, "Other"));
            seed.Debts.AddRange(CreateDebt(owner, "Mine"), CreateDebt(other, "Other"));
            seed.Transactions.AddRange(CreateTransaction(owner), CreateTransaction(other));
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));

        Assert.All(await db.Accounts.ToListAsync(), item => Assert.Equal(owner, item.OwnerId));
        Assert.All(await db.Categories.ToListAsync(), item => Assert.Equal(owner, item.OwnerId));
        Assert.All(await db.Debts.ToListAsync(), item => Assert.Equal(owner, item.OwnerId));
        Assert.All(await db.Transactions.ToListAsync(), item => Assert.Equal(owner, item.OwnerId));
    }

    [Fact]
    public async Task SaveChanges_AssignsCurrentOwnerToNewEntities()
    {
        var owner = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));
        var account = CreateAccount(Guid.Empty, "New");
        db.Accounts.Add(account);

        await db.SaveChangesAsync();

        Assert.Equal(owner, account.OwnerId);
    }

    private static Account CreateAccount(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, Type = AccountType.Cash, Currency = "HUF" };
    private static Category CreateCategory(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, Type = CategoryType.Expense, Icon = "food-1" };
    private static Debt CreateDebt(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, CounterpartyName = "Test", Currency = "HUF", OriginalAmount = 100, StartDate = new DateOnly(2026, 1, 1) };
    private static Transaction CreateTransaction(Guid owner) => new() { Id = Guid.NewGuid(), OwnerId = owner, Type = TransactionType.Adjustment, Amount = 100, AdjustmentDirection = AdjustmentDirection.Increase, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" };

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
