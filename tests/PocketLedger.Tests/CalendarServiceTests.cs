using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class CalendarServiceTests
{
    private readonly Guid ownerId = Guid.NewGuid();

    [Fact]
    public async Task DailyTotals_TreatAdjustmentsAsIncomeAndExpenses()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2026, 8, 28);
        db.Transactions.AddRange(
            Transaction(TransactionType.Income, 100, date),
            Transaction(TransactionType.Expense, 40, date),
            Transaction(TransactionType.Adjustment, 25, date, AdjustmentDirection.Increase),
            Transaction(TransactionType.Adjustment, 10, date, AdjustmentDirection.Decrease));
        await db.SaveChangesAsync();

        var result = await new CalendarService(db).GetMonthAsync(2026, 8, CancellationToken.None);

        var total = Assert.Single(result[date].Totals);
        Assert.Equal(125, total.Income);
        Assert.Equal(50, total.Expenses);
        Assert.Equal(75, total.Balance);
    }

    [Fact]
    public async Task DailyTotals_ExcludeTransfersAndKeepCurrenciesSeparate()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2026, 8, 28);
        db.Transactions.AddRange(
            Transaction(TransactionType.Income, 100, date, currency: "HUF"),
            Transaction(TransactionType.Expense, 20, date, currency: "EUR"),
            Transaction(TransactionType.Transfer, 500, date, currency: "HUF"));
        await db.SaveChangesAsync();

        var result = await new CalendarService(db).GetMonthAsync(2026, 8, CancellationToken.None);

        Assert.Collection(result[date].Totals.OrderBy(item => item.Currency),
            eur => { Assert.Equal("EUR", eur.Currency); Assert.Equal(0, eur.Income); Assert.Equal(20, eur.Expenses); Assert.Equal(-20, eur.Balance); },
            huf => { Assert.Equal("HUF", huf.Currency); Assert.Equal(100, huf.Income); Assert.Equal(0, huf.Expenses); Assert.Equal(100, huf.Balance); });
    }

    [Fact]
    public async Task DailyTotals_ExcludeValidAccountlessDebtEntries()
    {
        await using var db = CreateDb();
        var date = new DateOnly(2026, 8, 28);
        db.Transactions.Add(Transaction(TransactionType.DebtEntry, 100, date, debtOperationType: DebtOperationType.ManualCorrectionIncrease));
        await db.SaveChangesAsync();

        var result = await new CalendarService(db).GetMonthAsync(2026, 8, CancellationToken.None);

        var day = result[date];
        var total = Assert.Single(day.Totals);
        Assert.Equal(1, day.TransactionCount);
        Assert.Equal(0, total.Income);
        Assert.Equal(0, total.Expenses);
        Assert.Equal(0, total.Balance);
    }

    private PocketLedgerDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
    }

    private Transaction Transaction(TransactionType type, decimal amount, DateOnly date, AdjustmentDirection? direction = null, string currency = "HUF", DebtOperationType? debtOperationType = null) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        Type = type,
        Amount = amount,
        TransactionDate = date,
        AdjustmentDirection = direction ?? AdjustmentDirection.Increase,
        SourceCurrency = currency,
        DebtOperationType = debtOperationType
    };

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }
}
