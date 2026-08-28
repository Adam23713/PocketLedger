using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class TransactionServiceTests
{
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
