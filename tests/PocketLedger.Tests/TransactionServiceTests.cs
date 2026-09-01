using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class TransactionServiceTests
{
    [Fact]
    public async Task CalculateMainBalance_GroupsCurrentAccountBalancesByCurrency()
    {
        var ownerId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestCurrentUser(ownerId));
        var hufAccount = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Forint", Type = AccountType.BankAccount, Currency = "HUF", InitialBalance = 100m, IncludeInMainBalance = true };
        var eurAccount = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Euro", Type = AccountType.BankAccount, Currency = "EUR", InitialBalance = 100m, IncludeInMainBalance = true };
        var excludedAccount = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Excluded", Type = AccountType.Cash, Currency = "HUF", InitialBalance = 500m, IncludeInMainBalance = false };
        db.Accounts.AddRange(hufAccount, eurAccount, excludedAccount);
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), OwnerId = ownerId, AccountId = hufAccount.Id, Type = TransactionType.Income, Amount = 25m, SourceCurrency = "HUF", TransactionDate = new DateOnly(2026, 9, 1) });
        await db.SaveChangesAsync();
        var service = new TransactionService(db);

        var balances = await service.CalculateMainBalanceAsync(CancellationToken.None);

        Assert.Equal([new CurrencyBalance("EUR", 100m), new CurrencyBalance("HUF", 125m)], balances);
    }

    [Fact]
    public async Task CreateTransfer_RecalculatesInvariantTargetAmountUsingTargetCurrencyPrecision()
    {
        var ownerId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestCurrentUser(ownerId));
        var source = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Forint", Type = AccountType.BankAccount, Currency = "HUF" };
        var target = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Euro", Type = AccountType.BankAccount, Currency = "EUR" };
        db.Accounts.AddRange(source, target);
        await db.SaveChangesAsync();
        var service = new TransactionService(db);

        var transaction = await service.CreateAsync(new Transaction
        {
            Type = TransactionType.Transfer, AccountId = source.Id, TargetAccountId = target.Id, Amount = 12.34m, TargetAmount = 999m, ExchangeRate = 1.2345m,
            TransactionDate = new DateOnly(2026, 8, 28), TransactionTime = new TimeOnly(12, 0), OccurredAtUtc = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero)
        }, CancellationToken.None);

        Assert.Equal(15.23m, transaction.TargetAmount);
        Assert.Equal("HUF", transaction.SourceCurrency);
        Assert.Equal("EUR", transaction.TargetCurrency);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
