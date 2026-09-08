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
    public void Projection_UsesActiveSchedulesRegardlessOfAutomationProcessing()
    {
        var account = Account("HUF", 1000);
        var recurring = Recurring(account, 100);
        recurring.AutomationStartsOn = Month.AddMonths(1);
        var result = Calculate(Month, [account], [], [recurring]);
        Assert.Equal(1100, Assert.Single(result.Accounts).ClosingBalance);
        Assert.Equal(100, Assert.Single(result.Totals).Income);
        Assert.Equal(PlannerEventSource.Recurring, Assert.Single(result.Events).Source);
        recurring.Enabled = false;
        Assert.Empty(Calculate(Month, [account], [], [recurring]).Events);
    }

    [Fact]
    public void Projection_RespectsScheduleEndAndMonthEndDates()
    {
        var account = Account("EUR", 100);
        var recurring = Recurring(account, 10);
        recurring.FirstOccurrence = new DateOnly(2026, 1, 31);
        Assert.Equal(new DateOnly(2026, 9, 30), Assert.Single(Calculate(Month, [account], [], [recurring]).Events).Date);
        recurring.LastOccurrence = Month.AddDays(-1);
        Assert.Empty(Calculate(Month, [account], [], [recurring]).Events);
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
        var result = Calculate(Month, [huf, eur], [expense, transfer], []);
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
        var result = Calculate(Month, [main, savings], [budget, savingsBudget], []);
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
        var result = Calculate(Month, [main, savings], [transfer], []);
        Assert.Equal(700, Assert.Single(result.Totals).Available);
        Assert.Equal(0, Assert.Single(result.Totals).Expenses);
        Assert.Equal(300, result.Accounts.Single(item => item.Id == savings.Id).ClosingBalance);
        Assert.Null(result.Accounts.Single(item => item.Id == savings.Id).ChangePercent);
    }

    [Fact]
    public void Projection_UsesOnlySelectedMonthAndItsOpeningSettings()
    {
        var account = Account("HUF", 1000);
        var oldPlan = Plan(account, TransactionType.Expense, 20, Month.AddDays(10));
        var result = PlannerProjection.Calculate(Month.AddMonths(2), Today, [account], new Dictionary<Guid, decimal> { [account.Id] = 1050 }, [new(account.Id, false, 2000)], [oldPlan], [Recurring(account, 100)]);
        var balance = Assert.Single(result.Accounts);
        Assert.Equal(1050, balance.CurrentBalance);
        Assert.Equal(2000, balance.OpeningBalance);
        Assert.Equal(2100, balance.ClosingBalance);
        Assert.Equal(0, Assert.Single(result.Totals).PreviousClosingBalance);
    }

    [Fact]
    public void Projection_PreviousComparisonUsesSavedPlan()
    {
        var account = Account("HUF", 1000);
        var before = Calculate(Month, [account], [], [Recurring(account, 100)]);
        var result = PlannerProjection.Calculate(Month.AddMonths(1), Today, [account], new Dictionary<Guid, decimal> { [account.Id] = 1000 }, [], [], [], before);
        Assert.Equal(1000, Assert.Single(result.Accounts).OpeningBalance);
        Assert.Equal(1100, Assert.Single(result.Totals).PreviousClosingBalance);
        Assert.Equal(100, Assert.Single(result.Totals).PreviousIncome);
    }

    [Fact]
    public void Projection_SortsMovementsAndIncludesUndatedIncomeInClosing()
    {
        var account = Account("EUR", 0);
        var later = Plan(account, TransactionType.Expense, 20, Month.AddDays(20));
        var earlier = Plan(account, TransactionType.Income, 100, Month.AddDays(2));
        var undated = Plan(account, TransactionType.Income, 10, null);
        var result = Calculate(Month, [account], [later, earlier, undated], []);
        Assert.Equal(90, Assert.Single(result.Accounts).ClosingBalance);
        Assert.Equal(110, Assert.Single(result.Totals).Income);
        Assert.Equal(0, result.Points.Single(point => point.Date == Month.AddDays(1)).Balance);
        Assert.Equal(100, result.Points.Single(point => point.Date == Month.AddDays(2)).Balance);
        Assert.Equal(80, result.Points.Last().Balance);
    }

    [Fact]
    public void Projection_UsesFullActiveLoanScheduleWithoutRemainingDebtCalculation()
    {
        var account = Account("EUR", 1000);
        var debt = new Debt { Id = Guid.NewGuid(), OriginalAmount = 50, Status = DebtStatus.Active };
        var recurring = Recurring(account, 100);
        recurring.Type = TransactionType.Expense;
        recurring.DebtId = debt.Id;
        recurring.Debt = debt;
        recurring.DebtOperationType = DebtOperationType.Payment;
        var result = Calculate(Month.AddMonths(2), [account], [], [recurring]);
        Assert.Equal(100, Assert.Single(result.Events).Amount);
        Assert.Equal(900, Assert.Single(result.Accounts).ClosingBalance);
        debt.Status = DebtStatus.Closed;
        Assert.Empty(Calculate(Month, [account], [], [recurring]).Events);
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
        await using var foreign = new PocketLedgerDbContext(options, new CurrentUser(otherOwner), TestDates());
        var account = Account("EUR", 100);
        var category = new Category { Id = Guid.NewGuid(), Type = CategoryType.Expense };
        foreign.AddRange(account, category);
        await foreign.SaveChangesAsync();
        var input = Input(account, category);
        var id = await new PlannerService(foreign, new PlannerTestUserContext()).CreateAsync(input, default);
        await using var db = new PocketLedgerDbContext(options, new CurrentUser(owner), TestDates());
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
        var clock = new PlannerTestClock();
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options, new CurrentUser(owner), new UserDateProvider(clock));
        await db.Database.EnsureCreatedAsync();
        var account = Account("HUF", 1000);
        var category = new Category { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense, Icon = CategoryIcons.DefaultFor(CategoryType.Expense).Id };
        db.AddRange(account, category);
        await db.SaveChangesAsync();
        var service = new PlannerService(db, new PlannerTestUserContext(clock));
        var input = Input(account, category);
        var originalId = await service.CreateAsync(input, default);
        var backupService = new ImportExportService(db, null!, new PlannerTestUserContext(clock));
        var json = await backupService.ExportBackupAsync(default);
        Assert.Equal(4, BackupJson.Deserialize(json).Version);
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
        await Assert.ThrowsAsync<BusinessRuleException>(() => new AccountService(db, TimeProvider.System, new PlannerTestUserContext(clock), null!).DeleteAsync(restored.AccountId, default));
        await service.UpdateOpeningBalanceAsync(2026, 9, restored.AccountId, new(false, 2000), default);
        clock.Now = new(2026, 10, 1, 0, 1, 0, TimeSpan.Zero);
        await service.GetMonthAsync(2026, 10, default);
        var historyBackup = await backupService.ExportBackupAsync(default);
        await backupService.RestoreAsync(historyBackup, default);
        var historical = await service.GetMonthAsync(2026, 9, default);
        Assert.True(historical.IsClosed);
        Assert.Equal(2000, Assert.Single(historical.Accounts).OpeningBalance);
        Assert.Equal(100, Assert.Single(historical.Totals).Expenses);
        Assert.Equal(2000, Assert.Single((await service.GetMonthAsync(2026, 10, default)).Accounts).OpeningBalance);
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

    private static PlannerMonth Calculate(DateOnly month, IReadOnlyList<Account> accounts, IReadOnlyList<PlannerItem> plans, IReadOnlyList<RecurringTransaction> recurring) => PlannerProjection.Calculate(month, Today, accounts, accounts.ToDictionary(item => item.Id, item => item.InitialBalance), [], plans, recurring);
    private static IUserDateProvider TestDates() => new UserDateProvider(new PlannerTestClock());
    private static Account Account(string currency, decimal balance, bool main = true) => new() { Id = Guid.NewGuid(), Name = currency, Currency = currency, InitialBalance = balance, IncludeInMainBalance = main };
    private static Transaction Transaction(Account account, decimal amount, DateOnly date) => new() { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Income, Amount = amount, SourceCurrency = account.Currency, TransactionDate = date };
    private static RecurringTransaction Recurring(Account account, decimal amount) => new() { Id = Guid.NewGuid(), AccountId = account.Id, Type = TransactionType.Income, Amount = amount, FirstOccurrence = Month.AddDays(4), AutomationStartsOn = Month, Enabled = true, Frequency = RecurringFrequency.Monthly };
    private static PlannerItem Plan(Account account, TransactionType type, decimal amount, DateOnly? date) => new() { Id = Guid.NewGuid(), Month = Month, PlannedDate = date, AccountId = account.Id, Type = type, Amount = amount, AccountAmount = amount, Currency = account.Currency };
    private static PlannerItemInput Input(Account account, Category category) => new(Month, null, TransactionType.Expense, account.Id, null, category.Id, 100, account.Currency, 100, null, null);
    private static PocketLedgerDbContext Db(Guid owner) => new(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new CurrentUser(owner), TestDates());
    private sealed class CurrentUser(Guid id) : ICurrentUser { public Guid UserId => id; public bool IsAuthenticated => true; }
}

internal sealed class PlannerTestUserContext(PlannerTestClock? clock = null) : IUserContextService
{
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(clock is null ? new DateOnly(2026, 9, 8) : DateOnly.FromDateTime(clock.Now.UtcDateTime));
    public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => Task.FromResult(new UserPreference { DefaultCurrency = "HUF" });
    public string Format(decimal amount, string? currency) => $"{FormatNumber(amount, currency)} {currency}";
    public string FormatNumber(decimal amount, string? currency) => amount.ToString(currency == "HUF" ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);
    public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => Task.FromResult(Format(amount, currency));
    public MoneyInputFormat GetMoneyInputFormat(string currency) => new(currency == "HUF" ? 0 : 2, ".", ",");
    public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => Task.FromResult(new DateTimeOffset(date.ToDateTime(time), TimeSpan.Zero));
}

internal sealed class PlannerTestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
