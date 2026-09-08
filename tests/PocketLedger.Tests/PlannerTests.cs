using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using PocketLedger.Models;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class PlannerTests
{
    private static readonly DateOnly Month = new(2026, 9, 1);
    private static readonly DateOnly Today = new(2026, 9, 8);

    [Fact]
    public void Projection_UsesActualAndUnprocessedRecurringWithoutWritingTransactions()
    {
        var account = Account("HUF", 1000);
        var recurring = Recurring(account, 100);
        var actual = Transaction(account, 100, Month.AddDays(4));
        var occurrence = new RecurringTransactionOccurrence { RecurringTransactionId = recurring.Id, OccurrenceDate = actual.TransactionDate, TransactionId = actual.Id };
        var result = PlannerProjection.Calculate(Month, Today, [account], [actual], [], [recurring], [occurrence]);
        Assert.Equal(1100, Assert.Single(result.Accounts).ClosingBalance);
        Assert.Equal(100, Assert.Single(result.Totals).Income);
        Assert.Equal(PlannerEventSource.Actual, Assert.Single(result.Events).Source);

        var pending = PlannerProjection.Calculate(Month, Today, [account], [], [], [recurring], []);
        Assert.Equal(1100, Assert.Single(pending.Accounts).ClosingBalance);
        Assert.Equal(PlannerEventSource.Recurring, Assert.Single(pending.Events).Source);
    }

    [Fact]
    public void Projection_DoesNotResurrectDeletedOrDisabledRecurringOccurrences()
    {
        var account = Account("HUF", 1000);
        var recurring = Recurring(account, 100);
        var occurrence = new RecurringTransactionOccurrence { RecurringTransactionId = recurring.Id, OccurrenceDate = Month.AddDays(4) };
        var result = PlannerProjection.Calculate(Month, Today, [account], [], [], [recurring], [occurrence]);
        Assert.Empty(result.Events);
        recurring.Enabled = false;
        Assert.Empty(PlannerProjection.Calculate(Month, Today, [account], [], [], [recurring], []).Events);
    }

    [Fact]
    public void Projection_RespectsAutomationStartAndMonthEndSchedule()
    {
        var account = Account("EUR", 100);
        var recurring = Recurring(account, 10);
        recurring.FirstOccurrence = new DateOnly(2026, 1, 31);
        recurring.AutomationStartsOn = Month.AddDays(9);
        var result = PlannerProjection.Calculate(Month, Today, [account], [], [], [recurring], []);
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(result.Events).Date);
        recurring.AutomationStartsOn = new DateOnly(2026, 10, 1);
        Assert.Empty(PlannerProjection.Calculate(Month, Today, [account], [], [], [recurring], []).Events);
    }

    [Fact]
    public void Projection_ConvertsExpensesAndTransfersWithoutMixingCurrencies()
    {
        var huf = Account("HUF", 100000);
        var eur = Account("EUR", 50);
        var expense = Plan(huf, TransactionType.Expense, 8000, Month.AddDays(10));
        expense.Amount = 20;
        expense.Currency = "EUR";
        var transfer = Plan(huf, TransactionType.Transfer, 40000, Month.AddDays(11));
        transfer.TargetAccountId = eur.Id;
        transfer.TargetAmount = 100;
        var result = PlannerProjection.Calculate(Month, Today, [huf, eur], [], [expense, transfer], [], []);
        var hufTotal = result.Totals.Single(item => item.Currency == "HUF");
        var eurTotal = result.Totals.Single(item => item.Currency == "EUR");
        Assert.Equal(52000, hufTotal.ClosingBalance);
        Assert.Equal(8000, hufTotal.Expenses);
        Assert.Equal(0, hufTotal.Income);
        Assert.Equal(150, eurTotal.ClosingBalance);
        Assert.Equal(0, eurTotal.Income);
        Assert.Equal(0, eurTotal.Expenses);
        Assert.Equal(20, result.Events.Single(item => item.Id == expense.Id).Amount);
        Assert.Equal(52000, result.Points.Last(point => point.Currency == "HUF").Balance);
    }

    [Fact]
    public void Projection_UndatedBudgetOnlyReducesAvailableAndHonorsMainBalanceFlag()
    {
        var main = Account("HUF", 1000);
        var savings = Account("HUF", 5000, false);
        var budget = Plan(main, TransactionType.Expense, 300, null);
        var savingsBudget = Plan(savings, TransactionType.Expense, 200, null);
        var result = PlannerProjection.Calculate(Month, Today, [main, savings], [], [budget, savingsBudget], [], []);
        var total = Assert.Single(result.Totals);
        Assert.Equal(1000, total.ClosingBalance);
        Assert.Equal(700, total.Available);
        Assert.Equal(500, total.Expenses);
        Assert.All(result.Points, point => Assert.Equal(1000, point.Balance));
        Assert.All(result.Events, item => Assert.Null(item.Date));
    }

    [Fact]
    public void Projection_TransferToSavingsReducesAvailableWithoutBecomingExpense()
    {
        var main = Account("HUF", 1000);
        var savings = Account("HUF", 0, false);
        var transfer = Plan(main, TransactionType.Transfer, 300, Month);
        transfer.TargetAccountId = savings.Id;
        transfer.TargetAmount = 300;
        var result = PlannerProjection.Calculate(Month, Today, [main, savings], [], [transfer], [], []);
        Assert.Equal(700, Assert.Single(result.Totals).Available);
        Assert.Equal(0, Assert.Single(result.Totals).Expenses);
        Assert.Equal(300, result.Accounts.Single(item => item.Id == savings.Id).ClosingBalance);
        Assert.Null(result.Accounts.Single(item => item.Id == savings.Id).ChangePercent);
    }

    [Fact]
    public void Projection_FutureOpeningCarriesCurrentAndInterveningMonthsForward()
    {
        var account = Account("HUF", 1000);
        var actual = Transaction(account, 50, Month.AddDays(-10));
        var recurring = Recurring(account, 100);
        var plan = Plan(account, TransactionType.Expense, 20, Month.AddDays(10));
        var result = PlannerProjection.Calculate(Month.AddMonths(2), Today, [account], [actual], [plan], [recurring], []);
        var balance = Assert.Single(result.Accounts);
        Assert.Equal(1050, balance.CurrentBalance);
        Assert.Equal(1230, balance.OpeningBalance);
        Assert.Equal(1330, balance.ClosingBalance);
        Assert.Equal(1230, Assert.Single(result.Totals).PreviousClosingBalance);
        Assert.Equal(100, Assert.Single(result.Totals).PreviousIncome);
    }

    [Fact]
    public void Projection_HistoricalPlansDoNotChangeCurrentMonthOpening()
    {
        var account = Account("HUF", 1000);
        var oldPlan = Plan(account, TransactionType.Expense, 200, Month.AddDays(-5));
        oldPlan.Month = Month.AddMonths(-1);
        var actual = Transaction(account, 100, Month.AddDays(-10));
        var result = PlannerProjection.Calculate(Month, Today, [account], [actual], [oldPlan], [], []);
        Assert.Equal(1100, Assert.Single(result.Accounts).OpeningBalance);
        Assert.Equal(900, Assert.Single(result.Totals).PreviousClosingBalance);
    }

    [Fact]
    public void Projection_SortsDailyMovementsAndIncludesAdjustmentsWithoutReportingIncome()
    {
        var account = Account("EUR", 0);
        var later = Plan(account, TransactionType.Expense, 20, Month.AddDays(20));
        var earlier = Plan(account, TransactionType.Income, 100, Month.AddDays(2));
        var correction = Transaction(account, 10, Month.AddDays(1));
        correction.Type = TransactionType.Adjustment;
        correction.AdjustmentDirection = AdjustmentDirection.Increase;
        var result = PlannerProjection.Calculate(Month, Today, [account], [correction], [later, earlier], [], []);
        Assert.Equal(90, Assert.Single(result.Accounts).ClosingBalance);
        Assert.Equal(100, Assert.Single(result.Totals).Income);
        Assert.Equal(10, result.Points.Single(point => point.Date == Month.AddDays(1)).Balance);
        Assert.Equal(110, result.Points.Single(point => point.Date == Month.AddDays(2)).Balance);
    }

    [Fact]
    public void Projection_CapsAutomaticDebtPaymentsAtRemainingDebt()
    {
        var account = Account("EUR", 1000);
        var debt = new Debt { Id = Guid.NewGuid(), OriginalAmount = 150, Status = DebtStatus.Active };
        var recurring = Recurring(account, 100);
        recurring.Type = TransactionType.Expense;
        recurring.DebtId = debt.Id;
        recurring.DebtOperationType = DebtOperationType.Payment;
        var result = PlannerProjection.Calculate(Month.AddMonths(1), Today, [account], [], [], [recurring], [], [debt]);
        Assert.Equal(50, Assert.Single(result.Events).Amount);
        Assert.Equal(850, Assert.Single(result.Accounts).ClosingBalance);
        var paidOff = PlannerProjection.Calculate(Month.AddMonths(2), Today, [account], [], [], [recurring], [], [debt]);
        Assert.Empty(paidOff.Events);
        Assert.Equal(850, Assert.Single(paidOff.Accounts).ClosingBalance);
    }

    [Fact]
    public async Task Service_CrudPersistsPlansWithoutCreatingActualTransactions()
    {
        await using var db = Db(Guid.NewGuid());
        var account = Account("HUF", 1000);
        var category = new Category { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense };
        db.AddRange(account, category);
        await db.SaveChangesAsync();
        var service = new PlannerService(db, new PlannerTestUserContext());
        var input = Input(account, category);
        var id = await service.CreateAsync(input, default);
        Assert.Equal(input, await service.GetByIdAsync(id, default));
        Assert.Empty(db.Transactions);
        Assert.Empty(db.RecurringTransactionOccurrences);
        var result = await service.GetMonthAsync(2026, 9, default);
        Assert.Equal(900, Assert.Single(result.Totals).Available);
        await service.UpdateAsync(id, input with { Amount = 200, AccountAmount = 200 }, default);
        Assert.Equal(200, (await service.GetByIdAsync(id, default))!.Amount);
        await service.DeleteAsync(id, default);
        Assert.Null(await service.GetByIdAsync(id, default));
        Assert.Empty(db.Transactions);
    }

    [Fact]
    public async Task Service_RejectsForeignReferencesAndHidesOtherOwnersPlans()
    {
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var name = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(name).Options;
        await using var foreign = new PocketLedgerDbContext(options, new CurrentUser(otherOwner));
        var account = Account("EUR", 100);
        var category = new Category { Id = Guid.NewGuid(), Type = CategoryType.Expense };
        foreign.AddRange(account, category);
        await foreign.SaveChangesAsync();
        var input = Input(account, category);
        var id = await new PlannerService(foreign, new PlannerTestUserContext()).CreateAsync(input, default);
        await using var db = new PocketLedgerDbContext(options, new CurrentUser(owner));
        var service = new PlannerService(db, new PlannerTestUserContext());
        Assert.Null(await service.GetByIdAsync(id, default));
        Assert.Empty((await service.GetMonthAsync(2026, 9, default)).Events);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(input, default));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.UpdateAsync(id, input, default));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.DeleteAsync(id, default));
        var forged = new PlannerItem { Id = id, OwnerId = owner, Month = Month, AccountId = account.Id };
        db.Update(forged);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Rules_RequireCompatibleCategoriesDatesAndExplicitConversion()
    {
        var account = Account("HUF", 0);
        var category = new Category { Id = Guid.NewGuid(), Type = CategoryType.Expense };
        var input = Input(account, category);
        PlannerRules.Validate(input, account, null, category);
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { AccountAmount = 99 }, account, null, category));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { PlannedDate = Month.AddMonths(1) }, account, null, category));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { Month = Month.AddDays(1) }, account, null, category));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { Type = TransactionType.Income }, account, null, category));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { Amount = 1.12345m }, account, null, category));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { Currency = "XYZ" }, account, null, category));
        PlannerRules.Validate(input with { Currency = "EUR", Amount = 1, AccountAmount = 400 }, account, null, category);
    }

    [Fact]
    public void Rules_ValidateTransferEndpointsAndAmounts()
    {
        var source = Account("HUF", 1000);
        var target = Account("EUR", 0);
        var input = new PlannerItemInput(Month, Month, TransactionType.Transfer, source.Id, target.Id, null, 400, "HUF", 400, 1, null);
        PlannerRules.Validate(input, source, target, null);
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { TargetAmount = null }, source, target, null));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input with { PlannedDate = null }, source, target, null));
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input, source, source, null));
        target.Currency = "HUF";
        Assert.Throws<BusinessRuleException>(() => PlannerRules.Validate(input, source, target, null));
    }

    [Theory]
    [InlineData(0, 9)]
    [InlineData(2026, 13)]
    [InlineData(9999, 12)]
    public async Task Service_RejectsInvalidMonths(int year, int month)
    {
        await using var db = Db(Guid.NewGuid());
        await Assert.ThrowsAsync<BusinessRuleException>(() => new PlannerService(db, new PlannerTestUserContext()).GetMonthAsync(year, month, default));
    }

    [Fact]
    public async Task Backup_RoundTripRestoresPlansAndReferencesWithoutActualTransactions()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var owner = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options, new CurrentUser(owner));
        await db.Database.EnsureCreatedAsync();
        var account = Account("HUF", 1000);
        var category = new Category { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense, Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id };
        db.AddRange(account, category);
        await db.SaveChangesAsync();
        var service = new PlannerService(db, new PlannerTestUserContext());
        var input = Input(account, category);
        var originalId = await service.CreateAsync(input, default);
        var backupService = new ImportExportService(db, null!, new PlannerTestUserContext());
        var json = await backupService.ExportBackupAsync(default);
        Assert.Equal(3, BackupJson.Deserialize(json).Version);
        Assert.True(backupService.PreviewRestore(json).IsValid);
        await backupService.RestoreAsync(json, default);
        var restored = await db.PlannerItems.AsNoTracking().SingleAsync();
        Assert.NotEqual(originalId, restored.Id);
        Assert.NotEqual(account.Id, restored.AccountId);
        Assert.Equal(owner, restored.OwnerId);
        Assert.Equal(100, restored.AccountAmount);
        Assert.Equal((await db.Categories.SingleAsync()).Id, restored.CategoryId);
        Assert.Empty(await db.Transactions.ToListAsync());
        Assert.Equal(900, Assert.Single((await service.GetMonthAsync(2026, 9, default)).Totals).Available);
        await Assert.ThrowsAsync<BusinessRuleException>(() => new CategoryService(db).DeleteAsync(restored.CategoryId!.Value, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => new AccountService(db, TimeProvider.System, new PlannerTestUserContext(), null!).DeleteAsync(restored.AccountId, default));
    }

    [Fact]
    public void Backup_ValidatesPlannerReferencesAndSupportsLegacyBackups()
    {
        var account = Account("HUF", 1000);
        var category = new Category { Id = Guid.NewGuid(), Type = CategoryType.Expense };
        var backup = new PocketLedgerBackup(2, DateTimeOffset.UtcNow,
            [new AccountBackup(account.Id, "Bank", AccountType.BankAccount, "HUF", 1000, null, 0, true, true, true)],
            [], [], [], PlannerItems: [new PlannerItemBackup(Guid.NewGuid(), Input(account, category))]);
        Assert.Contains(BackupValidator.Validate(backup), error => error.Contains("PlannerItem"));
        var legacy = backup with { PlannerItems = null };
        Assert.DoesNotContain(BackupValidator.Validate(legacy), error => error.Contains("PlannerItem"));
        Assert.Equal(backup.PlannerItems![0], BackupJson.Deserialize(BackupJson.Serialize(backup)).PlannerItems![0]);
    }

    private static Account Account(string currency, decimal balance, bool main = true) => new() { Id = Guid.NewGuid(), Name = currency, Currency = currency, InitialBalance = balance, IncludeInMainBalance = main };
    private static Transaction Transaction(Account account, decimal amount, DateOnly date) => new() { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Income, Amount = amount, SourceCurrency = account.Currency, TransactionDate = date };
    private static RecurringTransaction Recurring(Account account, decimal amount) => new() { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Income, Amount = amount, FirstOccurrence = Month.AddDays(4), AutomationStartsOn = Month, Enabled = true, Frequency = RecurringFrequency.Monthly };
    private static PlannerItem Plan(Account account, TransactionType type, decimal amount, DateOnly? date) => new() { Id = Guid.NewGuid(), Month = Month, PlannedDate = date, AccountId = account.Id, Type = type, Amount = amount, AccountAmount = amount, Currency = account.Currency };
    private static PlannerItemInput Input(Account account, Category category) => new(Month, null, TransactionType.Expense, account.Id, null, category.Id, 100, account.Currency, 100, null, null);
    private static PocketLedgerDbContext Db(Guid owner) => new(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new CurrentUser(owner));
    private sealed class CurrentUser(Guid id) : ICurrentUser { public Guid UserId => id; public bool IsAuthenticated => true; }
}

internal sealed class PlannerTestUserContext : IUserContextService
{
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DateOnly(2026, 9, 8));
    public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserPreference { DefaultCurrency = "HUF" });
    public string Format(decimal amount, string? currency) => $"{FormatNumber(amount, currency)} {currency}";
    public string FormatNumber(decimal amount, string? currency) => amount.ToString(currency == "HUF" ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);
    public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => Task.FromResult(Format(amount, currency));
    public MoneyInputFormat GetMoneyInputFormat(string currency) => new(currency == "HUF" ? 0 : 2, ".", ",");
    public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => Task.FromResult(new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero));
}
