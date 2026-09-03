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

    [Fact]
    public async Task Summary_DistinguishesAdjustmentsDebtOperationsAndUncategorizedTransactions()
    {
        var ownerId = Guid.NewGuid();
        await using var db = CreateDb(ownerId);
        var date = new DateOnly(2026, 1, 10);
        db.Transactions.AddRange(
            CreateTransaction(ownerId, date, "HUF", TransactionType.Adjustment, AdjustmentDirection.Increase),
            CreateTransaction(ownerId, date, "HUF", TransactionType.Adjustment, AdjustmentDirection.Decrease),
            CreateTransaction(ownerId, date, "HUF", TransactionType.Expense, debtOperationType: DebtOperationType.Payment),
            CreateTransaction(ownerId, date, "HUF", TransactionType.Expense, debtOperationType: DebtOperationType.EarlyRepayment),
            CreateTransaction(ownerId, date, "HUF", TransactionType.Income, debtOperationType: DebtOperationType.ReceivedRepayment),
            CreateTransaction(ownerId, date, "HUF", TransactionType.Expense));
        await db.SaveChangesAsync();
        var service = CreateService(db, ownerId);

        var summary = await service.GetSummaryAsync(2026, 1, "HUF", CancellationToken.None);

        Assert.Contains(summary.IncomeByCategory, item => item.Name == "Adjustment increase" && item.Amount == 100);
        Assert.Contains(summary.IncomeByCategory, item => item.Name == "Received repayment" && item.Amount == 100);
        Assert.Contains(summary.ExpenseByCategory, item => item.Name == "Adjustment decrease" && item.Amount == 100);
        Assert.Contains(summary.ExpenseByCategory, item => item.Name == "Loan repayment" && item.Amount == 200);
        Assert.Contains(summary.ExpenseByCategory, item => item.Name == "Uncategorized" && item.Amount == 100);
        Assert.Contains(summary.ExpenseMainCategories, item => item.Name == "Adjustment decrease" && item.Amount == 100);
        Assert.Contains(summary.ExpenseMainCategories, item => item.Name == "Loan repayment" && item.Amount == 200);
        Assert.Contains(summary.ExpenseMainCategories, item => item.Name == "Uncategorized" && item.Amount == 100);
    }

    [Fact]
    public async Task Summary_MapsValidAccountlessDebtEntrySemantics()
    {
        var ownerId = Guid.NewGuid();
        await using var db = CreateDb(ownerId);
        var date = new DateOnly(2026, 1, 10);
        db.Transactions.Add(CreateTransaction(ownerId, date, "HUF", TransactionType.DebtEntry, debtOperationType: DebtOperationType.ManualCorrectionIncrease));
        await db.SaveChangesAsync();
        var service = CreateService(db, ownerId);

        var summary = await service.GetSummaryAsync(2026, 1, "HUF", CancellationToken.None);

        Assert.Equal(0, summary.Income);
        Assert.Equal(0, summary.Expenses);
        Assert.Equal(0, summary.Savings);
        Assert.Equal(0, summary.Balance);
        var january = Assert.Single(summary.MonthlyTrend, item => item.Year == 2026 && item.Month == 1);
        Assert.Equal(0, january.Income);
        Assert.Equal(0, january.Expenses);
        Assert.Equal(0, january.Balance);
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
        db.UserPreferences.Add(new UserPreference { UserId = ownerId, DefaultCurrency = "HUF", TimeZoneId = "Europe/Budapest" });
        db.SaveChanges();
        return db;
    }

    private static Transaction CreateTransaction(Guid ownerId, DateOnly date, string currency, TransactionType type, AdjustmentDirection? adjustmentDirection = null, DebtOperationType? debtOperationType = null) => new()
    {
        Id = Guid.NewGuid(), OwnerId = ownerId, TransactionDate = date, TransactionTime = new TimeOnly(12, 0), OccurredAtUtc = new DateTimeOffset(date.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
        Type = type, Amount = 100, SourceCurrency = currency, AdjustmentDirection = adjustmentDirection, DebtOperationType = debtOperationType
    };

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
