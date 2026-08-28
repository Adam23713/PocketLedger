using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class StatisticsServiceTests
{
    [Fact]
    public async Task AvailableCurrencies_ContainsOnlyStatisticsTransactionsFromSelectedMonth()
    {
        var ownerId = Guid.NewGuid();
        await using var db = CreateDb(ownerId);
        db.Transactions.AddRange(
            CreateTransaction(ownerId, new DateOnly(2026, 1, 2), "HUF", TransactionType.Expense),
            CreateTransaction(ownerId, new DateOnly(2026, 1, 3), "EUR", TransactionType.Income),
            CreateTransaction(ownerId, new DateOnly(2026, 1, 4), "USD", TransactionType.Transfer),
            CreateTransaction(ownerId, new DateOnly(2026, 2, 1), "USD", TransactionType.Expense));
        await db.SaveChangesAsync();
        var service = CreateService(db, ownerId);

        var january = await service.GetAvailableCurrenciesAsync(2026, 1, CancellationToken.None);
        var february = await service.GetAvailableCurrenciesAsync(2026, 2, CancellationToken.None);
        var march = await service.GetAvailableCurrenciesAsync(2026, 3, CancellationToken.None);

        Assert.Equal(["HUF", "EUR"], january);
        Assert.Equal(["USD"], february);
        Assert.Empty(march);
    }

    [Fact]
    public async Task AvailableCurrencies_RejectsInvalidMonth()
    {
        var ownerId = Guid.NewGuid();
        await using var db = CreateDb(ownerId);
        var service = CreateService(db, ownerId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.GetAvailableCurrenciesAsync(2026, 13, CancellationToken.None));
    }

    private static StatisticsService CreateService(PocketLedgerDbContext db, Guid ownerId)
    {
        var currentUser = new TestCurrentUser(ownerId);
        var userContext = new UserContextService(currentUser, db, TimeProvider.System);
        return new StatisticsService(db, new AccountService(db, TimeProvider.System, userContext));
    }

    private static PocketLedgerDbContext CreateDb(Guid ownerId)
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var currentUser = new TestCurrentUser(ownerId);
        var db = new PocketLedgerDbContext(options, currentUser);
        db.Users.Add(new ApplicationUser { Id = ownerId, UserName = "statistics", DefaultCurrency = "HUF", TimeZoneId = "Europe/Budapest" });
        db.SaveChanges();
        return db;
    }

    private static Transaction CreateTransaction(Guid ownerId, DateOnly date, string currency, TransactionType type) => new()
    {
        Id = Guid.NewGuid(), OwnerId = ownerId, TransactionDate = date, TransactionTime = new TimeOnly(12, 0), OccurredAtUtc = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        Type = type, Amount = 100, SourceCurrency = currency
    };

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
