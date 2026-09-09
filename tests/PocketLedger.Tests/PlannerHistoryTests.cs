using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Tests;

public class PlannerHistoryTests
{
    [Fact]
    public async Task PausePersistsAcrossMonthsAndResumeDoesNotChangeClosedHistory()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var id = await fixture.Service.CreateAsync(fixture.Input(new(2026, 9, 1)), default);
        await fixture.Service.SetPausedAsync(id, true, default);
        Assert.True((await fixture.Service.GetByIdAsync(id, default))!.IsPaused);
        Assert.Equal(0, Assert.Single(fixture.Saved(9).Totals).Expenses);
        fixture.Clock.Now = new(2026, 10, 1, 0, 1, 0, TimeSpan.Zero);
        var october = await fixture.ReadAsync(10);
        var copy = Assert.Single(october.Events.Where(item => item.Source == PlannerEventSource.Planned));
        Assert.True(copy.IsPaused);
        await fixture.Service.SetPausedAsync(copy.Id, false, default);
        Assert.Equal(50, Assert.Single((await fixture.ReadAsync(10)).Totals).Expenses);
        var september = await fixture.ReadAsync(9);
        Assert.True(Assert.Single(september.Events.Where(item => item.Source == PlannerEventSource.Planned)).IsPaused);
        Assert.Equal(0, Assert.Single(september.Totals).Expenses);
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.SetPausedAsync(id, false, default));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => fixture.Service.SetPausedAsync(fixture.Recurring.Id, true, default));
    }

    [Theory]
    [InlineData(TransactionType.Income)]
    [InlineData(TransactionType.Expense)]
    [InlineData(TransactionType.Transfer)]
    public async Task CreatingItemsRequiresADateForEveryType(TransactionType type)
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var input = fixture.Input(new DateOnly(2026, 9, 1)) with { Type = type, PlannedDate = null };
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.CreateAsync(input, default));
        Assert.Equal("A planned date is required.", error.Message);
        Assert.Empty(await fixture.Db.PlannerItems.ToListAsync());
    }

    [Fact]
    public async Task InclusionIsIndependentAndPersistsWithoutChangingOpeningBalance()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Account.IncludeInMainBalance = false;
        await fixture.Db.SaveChangesAsync();
        Assert.True(Assert.Single((await fixture.ReadAsync(9)).Accounts).IncludeInMainBalance);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, 2000), default);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(null, null, false), default);
        var result = await fixture.ReadAsync(9);
        Assert.False(Assert.Single(result.Accounts).IncludeInMainBalance);
        Assert.Equal(2000, Assert.Single(result.Accounts).OpeningBalance);
        Assert.Equal(0, Assert.Single(result.Totals).ClosingBalance);
        Assert.Empty(result.Points);
        Assert.False(Assert.Single((await fixture.ReadAsync(10)).Accounts).IncludeInMainBalance);
        Assert.False(fixture.Account.IncludeInMainBalance);
    }

    [Fact]
    public async Task CurrentScheduleChangesRefreshSavedPlanAndClosedMonthsStayFrozen()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var september = await fixture.ReadAsync(9);
        fixture.Recurring.Amount = 200;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(200, Assert.Single(fixture.Saved(9).Totals).Income);
        fixture.Clock.Now = new(2026, 10, 1, 0, 1, 0, TimeSpan.Zero);
        fixture.Recurring.Amount = 300;
        fixture.Account.Name = "Renamed in October";
        fixture.Account.InitialBalance = 5000;
        await fixture.Db.SaveChangesAsync();
        september = await fixture.ReadAsync(9);
        Assert.True(september.IsClosed);
        Assert.Equal(200, Assert.Single(september.Totals).Income);
        Assert.Equal("Bank", Assert.Single(september.Accounts).Name);
        Assert.Equal(1000, Assert.Single(september.Accounts).OpeningBalance);
        var october = await fixture.ReadAsync(10);
        Assert.Equal(300, Assert.Single(october.Totals).Income);
        Assert.Equal(200, Assert.Single(october.Totals).PreviousIncome);
        Assert.Equal(5000, Assert.Single(october.Accounts).OpeningBalance);
        fixture.Recurring.Enabled = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Empty((await fixture.ReadAsync(10)).Events);
        Assert.Single((await fixture.ReadAsync(9)).Events);
    }

    [Fact]
    public async Task FirstWriteAfterDowntimeDoesNotApplyNewAmountsToMissedMonths()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ReadAsync(9);
        fixture.Clock.Now = new(2026, 12, 1, 0, 1, 0, TimeSpan.Zero);
        fixture.Recurring.Amount = 300;
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(100, Assert.Single((await fixture.ReadAsync(10)).Totals).Income);
        Assert.Equal(100, Assert.Single((await fixture.ReadAsync(11)).Totals).Income);
        Assert.Equal(300, Assert.Single((await fixture.ReadAsync(12)).Totals).Income);
    }

    [Fact]
    public async Task LedgerRowsAndProcessedOccurrencesNeverBecomePlanItems()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.ReadAsync(9);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, 2000), default);
        var actual = new Transaction { Id = Guid.NewGuid(), AccountId = fixture.Account.Id, Type = TransactionType.Income, Amount = 700, SourceCurrency = "HUF", TransactionDate = new(2026, 9, 5) };
        fixture.Db.Add(actual);
        fixture.Db.Add(new RecurringTransactionOccurrence { Id = Guid.NewGuid(), RecurringTransactionId = fixture.Recurring.Id, OccurrenceDate = new(2026, 9, 5), TransactionId = actual.Id });
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.ReadAsync(9);
        Assert.Equal(100, Assert.Single(result.Totals).Income);
        Assert.Equal(PlannerEventSource.Recurring, Assert.Single(result.Events).Source);
        Assert.Equal(2100, Assert.Single(result.Accounts).ClosingBalance);
        Assert.Equal(1700, Assert.Single(result.Accounts).CurrentBalance);
        fixture.Db.Remove(actual);
        await fixture.Db.SaveChangesAsync();
        Assert.Equal(2100, Assert.Single((await fixture.ReadAsync(9)).Accounts).ClosingBalance);
    }

    [Fact]
    public async Task OpeningBalanceSwitchPreservesCustomValueAndUsesMonthSpecificSettings()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, 2000), default);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 10, fixture.Account.Id, new(false, 3000), default);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(true, null), default);
        Assert.Equal(1000, Assert.Single((await fixture.ReadAsync(9)).Accounts).OpeningBalance);
        await fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, null), default);
        Assert.Equal(2000, Assert.Single((await fixture.ReadAsync(9)).Accounts).OpeningBalance);
        Assert.Equal(3000, Assert.Single((await fixture.ReadAsync(10)).Accounts).OpeningBalance);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => fixture.Service.UpdateOpeningBalanceAsync(2026, 9, Guid.NewGuid(), new(false, 1), default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, 1.12345m), default));
    }

    [Fact]
    public async Task RolloverCopiesUserItemsOnceClampsDatesAndPreservesPreparedMonths()
    {
        await using var fixture = new Fixture();
        fixture.Clock.Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        await fixture.SeedAsync();
        var january = new DateOnly(2026, 1, 1);
        var input = fixture.Input(january) with { PlannedDate = new(2026, 1, 31) };
        await fixture.Service.CreateAsync(input, default);
        var february = await fixture.ReadAsync(2);
        var copied = Assert.Single(february.Events);
        Assert.Equal(new DateOnly(2026, 2, 28), copied.Date);
        await fixture.Service.UpdateAsync(copied.Id, fixture.Input(january.AddMonths(1)) with { Amount = 80, AccountAmount = 80, PlannedDate = new(2026, 2, 28) }, default);
        // Preparing February explicitly changes its date to the 28th; January remains independent.
        fixture.Clock.Now = new(2026, 2, 1, 0, 1, 0, TimeSpan.Zero);
        Assert.Equal(80, Assert.Single((await fixture.ReadAsync(2)).Events).Amount);
        await fixture.ReadAsync(2);
        Assert.Equal(2, await fixture.Db.PlannerItems.CountAsync());
        Assert.Equal(50, Assert.Single((await fixture.ReadAsync(1)).Events).Amount);
        await fixture.Service.DeleteAsync(copied.Id, default);
        await fixture.ReadAsync(2);
        Assert.Empty((await fixture.ReadAsync(2)).Events);
    }

    [Fact]
    public async Task MissingMonthsAreCopiedAndMonthEndDayRecoversAfterFebruary()
    {
        await using var fixture = new Fixture();
        fixture.Clock.Now = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        await fixture.SeedAsync();
        await fixture.Service.CreateAsync(fixture.Input(new(2026, 1, 1)) with { PlannedDate = new(2026, 1, 31) }, default);
        fixture.Clock.Now = new(2026, 4, 1, 0, 1, 0, TimeSpan.Zero);
        await fixture.ReadAsync(4);
        Assert.Equal(4, await fixture.Db.PlannerItems.CountAsync());
        Assert.Equal(new DateOnly(2026, 2, 28), Assert.Single((await fixture.ReadAsync(2)).Events).Date);
        Assert.Equal(new DateOnly(2026, 3, 31), Assert.Single((await fixture.ReadAsync(3)).Events).Date);
        Assert.True((await fixture.ReadAsync(3)).IsClosed);
    }

    [Fact]
    public async Task PastMonthsRejectEveryMutationAndMissingHistoryIsNotInvented()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        var input = fixture.Input(new(2026, 9, 1));
        var id = await fixture.Service.CreateAsync(input, default);
        fixture.Clock.Now = new(2026, 10, 1, 0, 1, 0, TimeSpan.Zero);
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.CreateAsync(input, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.UpdateAsync(id, input with { Month = new(2026, 10, 1) }, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.DeleteAsync(id, default));
        await Assert.ThrowsAsync<BusinessRuleException>(() => fixture.Service.UpdateOpeningBalanceAsync(2026, 9, fixture.Account.Id, new(false, 0), default));
        var august = await fixture.ReadAsync(8);
        Assert.True(august.IsClosed);
        Assert.False(august.HasSnapshot);
        Assert.Empty(august.Events);
    }

    [Fact]
    public async Task WorkerClosesMonthsWithoutPageVisitInUsersTimeZone()
    {
        await using var fixture = new Fixture();
        await fixture.SeedAsync();
        fixture.Db.Add(new UserPreference { UserId = fixture.Owner, TimeZoneId = "Europe/Budapest" });
        await fixture.Db.SaveChangesAsync();
        await fixture.Service.CreateAsync(fixture.Input(new(2026, 9, 1)), default);
        fixture.Clock.Now = new(2026, 9, 30, 22, 1, 0, TimeSpan.Zero);
        var services = new ServiceCollection();
        services.AddSingleton<IRecurringTransactionProcessingDbContextFactory>(new RecurringTransactionProcessingDbContextFactory(fixture.Options));
        await using var provider = services.BuildServiceProvider();
        var worker = new PlannerMonthWorker(provider.GetRequiredService<IServiceScopeFactory>(), new UserDateProvider(fixture.Clock), Microsoft.Extensions.Options.Options.Create(new UserDateOptions()), NullLogger<PlannerMonthWorker>.Instance);
        await worker.CloseDueMonthsAsync(default);
        fixture.Db.ChangeTracker.Clear();
        Assert.True((await fixture.Db.PlannerMonths.SingleAsync(item => item.Month == new DateOnly(2026, 9, 1))).IsClosed);
        Assert.Single(await fixture.Db.PlannerItems.Where(item => item.Month == new DateOnly(2026, 10, 1)).ToListAsync());
        await worker.CloseDueMonthsAsync(default);
        Assert.Equal(2, await fixture.Db.PlannerItems.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid Owner { get; } = Guid.NewGuid();
        public PlannerTestClock Clock { get; } = new();
        public DbContextOptions<PocketLedgerDbContext> Options { get; } = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        public PocketLedgerDbContext Db { get; }
        public PlannerService Service { get; }
        public Account Account { get; } = new() { Id = Guid.NewGuid(), Name = "Bank", Currency = "HUF", InitialBalance = 1000, IncludeInMainBalance = true };
        public Category Category { get; } = new() { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense };
        public RecurringTransaction Recurring { get; }
        public Fixture()
        {
            Db = new(Options, new User(Owner), new UserDateProvider(Clock));
            Service = new(Db, new PlannerTestUserContext(Clock));
            Recurring = new() { Id = Guid.NewGuid(), AccountId = Account.Id, Type = TransactionType.Income, Amount = 100, Enabled = true, Frequency = RecurringFrequency.Monthly, FirstOccurrence = new(2026, 9, 5), AutomationStartsOn = new(2026, 9, 1) };
        }
        public async Task SeedAsync() { Db.AddRange(Account, Category, Recurring); await Db.SaveChangesAsync(); }
        public Task<PlannerMonth> ReadAsync(int month) => Service.GetMonthAsync(2026, month, default);
        public PlannerMonth Saved(int month) => PlannerHistory.Deserialize<PlannerMonth>(Db.PlannerMonths.Single(item => item.Month == new DateOnly(2026, month, 1)).SnapshotJson);
        public PlannerItemInput Input(DateOnly month) => new(month, month, TransactionType.Expense, Account.Id, null, Category.Id, 50, "HUF", 50, null, "Groceries");
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class User(Guid id) : ICurrentUser { public Guid UserId => id; public bool IsAuthenticated => true; }
}
