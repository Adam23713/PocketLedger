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
        var service = new TransactionService(db, new TestUserContext());

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
        var occurredAtUtc = new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
        var service = new TransactionService(db, new TestUserContext(occurredAtUtc));

        var transaction = await service.CreateAsync(new TransactionCreateInput(TransactionType.Transfer, source.Id, target.Id, 12.34m, 999m, 1.2345m, null, new DateOnly(2026, 8, 28), new TimeOnly(12, 0), null, null), CancellationToken.None);

        Assert.Equal(15.23m, transaction.TargetAmount);
        Assert.Equal("HUF", transaction.SourceCurrency);
        Assert.Equal("EUR", transaction.TargetCurrency);
        Assert.Equal(occurredAtUtc, transaction.OccurredAtUtc);
        Assert.Null(transaction.DebtId);
        Assert.Null(transaction.DebtOperationType);
    }

    [Fact]
    public async Task Update_DerivesServerManagedFieldsAndKeepsDebtFieldsEmpty()
    {
        var ownerId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestCurrentUser(ownerId));
        var account = new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Forint", Type = AccountType.BankAccount, Currency = "HUF" };
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, Type = TransactionType.Income, AccountId = account.Id, Amount = 10m, SourceCurrency = "MANIPULATED",
            TargetCurrency = "MANIPULATED", OccurredAtUtc = DateTimeOffset.MinValue, TransactionDate = new DateOnly(2026, 8, 1)
        };
        db.AddRange(account, transaction);
        await db.SaveChangesAsync();
        var occurredAtUtc = new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero);
        var service = new TransactionService(db, new TestUserContext(occurredAtUtc));

        await service.UpdateAsync(transaction.Id, new TransactionUpdateInput(TransactionType.Adjustment, account.Id, null, 25m, null, null, AdjustmentDirection.Increase, new DateOnly(2026, 9, 2), new TimeOnly(10, 30), null, " updated "), CancellationToken.None);

        var saved = await db.Transactions.SingleAsync(item => item.Id == transaction.Id);
        Assert.Equal("HUF", saved.SourceCurrency);
        Assert.Null(saved.TargetCurrency);
        Assert.Equal(occurredAtUtc, saved.OccurredAtUtc);
        Assert.Null(saved.DebtId);
        Assert.Null(saved.DebtOperationType);
        Assert.Null(saved.TargetAmount);
        Assert.Null(saved.ExchangeRate);
        Assert.Equal("updated", saved.Note);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class TestUserContext(DateTimeOffset occurredAtUtc = default) : IUserContextService
    {
        public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => Task.FromResult(occurredAtUtc);
        public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string Format(decimal amount, string? currency) => throw new NotSupportedException();
        public string FormatNumber(decimal amount, string? currency) => throw new NotSupportedException();
        public MoneyInputFormat GetMoneyInputFormat(string currency) => throw new NotSupportedException();
    }
}
