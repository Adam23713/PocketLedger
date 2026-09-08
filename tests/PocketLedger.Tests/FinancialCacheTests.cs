using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using StackExchange.Redis;

namespace PocketLedger.Tests;

public sealed class FinancialCacheFactAttribute : FactAttribute
{
    public FinancialCacheFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PL_TEST_POSTGRES")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PL_TEST_VALKEY")))
            Skip = "Set PL_TEST_POSTGRES and PL_TEST_VALKEY to run PostgreSQL/Valkey integration tests.";
    }
}

public sealed class FinancialCacheFixture : IAsyncLifetime
{
    private ServiceProvider? services;
    private readonly string databaseName = "pl_cache_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; private set; } = "";
    public IDistributedCache Store => services!.GetRequiredService<IDistributedCache>();

    public async Task InitializeAsync()
    {
        var postgres = Environment.GetEnvironmentVariable("PL_TEST_POSTGRES");
        var valkey = Environment.GetEnvironmentVariable("PL_TEST_VALKEY");
        if (string.IsNullOrWhiteSpace(postgres) || string.IsNullOrWhiteSpace(valkey)) return;
        await using var admin = new NpgsqlConnection(postgres);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin);
        await create.ExecuteNonQueryAsync();
        ConnectionString = new NpgsqlConnectionStringBuilder(postgres) { Database = databaseName }.ConnectionString;
        await using var db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseNpgsql(ConnectionString).Options);
        await db.Database.MigrateAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FinancialCache:ConnectionString"] = valkey }).Build();
        services = new ServiceCollection().AddLogging().AddFinancialCache(configuration).BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (services is not null) await services.DisposeAsync();
        if (ConnectionString.Length == 0) return;
        await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("PL_TEST_POSTGRES"));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}

public sealed class FinancialCacheTests(FinancialCacheFixture fixture) : IClassFixture<FinancialCacheFixture>
{
    private static readonly CancellationToken Token = CancellationToken.None;

    [FinancialCacheFact]
    public async Task MonthlyTransactions_HitRetainsNavigationDataAndMutationInvalidatesBothMonths()
    {
        await using var h = await CreateAsync();
        var transaction = await h.AddAsync(100);
        var first = await h.Transactions.GetForMonthAsync(2026, 9, Token);
        h.Counter.Reads = 0;
        var cached = await h.Transactions.GetForMonthAsync(2026, 9, Token);
        Assert.Equal(0, h.Counter.Reads);
        Assert.Equal(first.Single().Id, cached.Single().Id);
        Assert.Equal("Cash", cached.Single().Account!.Name);
        Assert.Equal("Food", cached.Single().Category!.Name);
        Assert.Empty(await h.Transactions.GetForMonthAsync(2026, 8, Token));

        transaction.TransactionDate = new DateOnly(2026, 8, 20);
        transaction.Amount = 250;
        await h.Db.SaveChangesAsync();
        Assert.Empty(await h.Transactions.GetForMonthAsync(2026, 9, Token));
        Assert.Equal(250, (await h.Transactions.GetForMonthAsync(2026, 8, Token)).Single().Amount);
        await h.Transactions.DeleteAsync(transaction.Id, Token);
        Assert.Empty(await h.Transactions.GetForMonthAsync(2026, 8, Token));
    }

    [FinancialCacheFact]
    public async Task FilteredTransactions_SeparatePagesFiltersAndDailyTotalsUseCache()
    {
        await using var h = await CreateAsync();
        await h.AddAsync(100, "Lunch 100%");
        await h.AddAsync(200, "Lunch 200%");
        var pageOne = new TransactionFilter { Year = 2026, Month = 9, PageSize = 1 };
        var pageTwo = new TransactionFilter { Year = 2026, Month = 9, PageSize = 1, Page = 2 };
        var filtered = new TransactionFilter { DateFrom = new DateOnly(2026, 8, 1), DateTo = new DateOnly(2026, 9, 30), Search = "100%", AmountTo = 150, AccountId = h.Account.Id };
        var first = await h.Transactions.GetFilteredAsync(pageOne, Token);
        var second = await h.Transactions.GetFilteredAsync(pageTwo, Token);
        Assert.NotEqual(first.Items.Single().Id, second.Items.Single().Id);
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(100, (await h.Transactions.GetFilteredAsync(filtered, Token)).Items.Single().Amount);
        Assert.Equal(300, (await h.Transactions.GetDailyTotalsAsync(pageOne, Token)).Single().Expenses);
        h.Counter.Reads = 0;
        await h.Transactions.GetFilteredAsync(pageOne, Token);
        await h.Transactions.GetFilteredAsync(pageTwo, Token);
        await h.Transactions.GetFilteredAsync(filtered, Token);
        await h.Transactions.GetDailyTotalsAsync(pageTwo, Token);
        Assert.Equal(0, h.Counter.Reads);
    }

    [FinancialCacheFact]
    public async Task StatisticsAndBalances_HitsAndCategoryAccountChangesInvalidate()
    {
        await using var h = await CreateAsync();
        await h.AddAsync(100);
        var first = await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token);
        Assert.Equal(100, first.Expenses);
        h.Counter.Reads = 0;
        var cached = await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token);
        Assert.Equal(0, h.Counter.Reads);
        Assert.Equal(first.ExpenseMainCategories.Single().Amount, cached.ExpenseMainCategories.Single().Amount);
        Assert.Equal(first.ExpenseMainCategories.Single().Subcategories.Single().Amount, cached.ExpenseMainCategories.Single().Subcategories.Single().Amount);
        Assert.Equal(900, (await h.Accounts.GetCurrentBalancesAsync(Token))[h.Account.Id]);
        h.Counter.Reads = 0;
        Assert.Equal(900, (await h.Transactions.CalculateAccountBalancesAsync(Token))[h.Account.Id]);
        Assert.Equal(0, h.Counter.Reads);
        Assert.Equal(0, (await h.Statistics.GetSummaryAsync(2026, 9, "EUR", Token)).Expenses);

        h.Category.Name = "Groceries";
        h.Account.InitialBalance = 2000;
        await h.Db.SaveChangesAsync();
        var updated = await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token);
        Assert.Equal("Groceries", updated.ExpenseByCategory.Single().Name);
        Assert.Equal(1900, updated.AccountBalances.Single().Balance);
        Assert.Equal(1900, (await h.Transactions.CalculateMainBalanceAsync(Token)).Single().Amount);
    }

    [FinancialCacheFact]
    public async Task Windows_AreTwoAndTwelveMonthsAndFollowUserMonthAtYearBoundary()
    {
        await using var h = await CreateAsync();
        Assert.True(await h.Cache.IncludesMonthAsync(2026, 8, 2, Token));
        Assert.False(await h.Cache.IncludesMonthAsync(2026, 7, 2, Token));
        Assert.True(await h.Cache.IncludesMonthAsync(2025, 10, 12, Token));
        Assert.False(await h.Cache.IncludesMonthAsync(2025, 9, 12, Token));
        Assert.False(await h.Cache.IncludesMonthAsync(2026, 10, 12, Token));
        await h.Statistics.GetSummaryAsync(2025, 10, "HUF", Token);
        h.Counter.Reads = 0;
        await h.Statistics.GetSummaryAsync(2025, 10, "HUF", Token);
        Assert.Equal(0, h.Counter.Reads);
        await h.Transactions.GetForMonthAsync(2026, 7, Token);
        h.Counter.Reads = 0;
        await h.Transactions.GetForMonthAsync(2026, 7, Token);
        Assert.True(h.Counter.Reads > 0);

        h.Clock.Now = new DateTimeOffset(2026, 12, 31, 23, 30, 0, TimeSpan.Zero);
        Assert.True(await h.Cache.IncludesMonthAsync(2027, 1, 2, Token));
        Assert.True(await h.Cache.IncludesMonthAsync(2026, 12, 2, Token));
        Assert.False(await h.Cache.IncludesMonthAsync(2026, 11, 2, Token));
        Assert.False(await h.Cache.IncludesFilterAsync(new TransactionFilter(), Token));
    }

    [FinancialCacheFact]
    public async Task TenantIsolation_AnotherUsersWriteDoesNotInvalidateOrExposeData()
    {
        await using var one = await CreateAsync();
        await using var two = await CreateAsync();
        await one.AddAsync(100);
        await two.AddAsync(200);
        Assert.Equal(100, (await one.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        Assert.Equal(200, (await two.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        await two.AddAsync(300);
        one.Counter.Reads = 0;
        Assert.Single(await one.Transactions.GetForMonthAsync(2026, 9, Token));
        Assert.Equal(0, one.Counter.Reads);
        Assert.Equal(2, (await two.Transactions.GetForMonthAsync(2026, 9, Token)).Count);
    }

    [FinancialCacheFact]
    public async Task FailedCacheReadsAndWrites_FallBackAndRecoveryCannotResurrectOldData()
    {
        await using var h = await CreateAsync();
        var transaction = await h.AddAsync(100);
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        h.Store.FailReads = true;
        transaction.Amount = 200;
        await h.Db.SaveChangesAsync();
        Assert.Equal(200, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        h.Store.FailReads = false;
        h.Store.FailWrites = true;
        Assert.Equal(200, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        h.Store.FailWrites = false;
        Assert.Equal(200, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        h.Counter.Reads = 0;
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        Assert.Equal(0, h.Counter.Reads);
    }

    [FinancialCacheFact]
    public async Task TransactionCommitIsAtomicAndRollbackDoesNotPublishUncommittedData()
    {
        await using var h = await CreateAsync();
        var transaction = await h.AddAsync(100);
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        await using var reader = h.NewReader();
        await using (var write = await h.Db.Database.BeginTransactionAsync())
        {
            transaction.Amount = 200;
            await h.Db.SaveChangesAsync();
            Assert.Equal(200, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
            Assert.Equal(100, (await reader.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
            await write.RollbackAsync();
        }
        Assert.Equal(100, (await reader.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        await using (var write = await h.Db.Database.BeginTransactionAsync())
        {
            transaction.Amount = 300;
            await h.Db.SaveChangesAsync();
            await write.CommitAsync();
        }
        Assert.Equal(300, (await reader.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
    }

    [FinancialCacheFact]
    public async Task FillRacingAWrite_CannotPoisonTheNewRevision()
    {
        await using var h = await CreateAsync();
        var transaction = await h.AddAsync(100);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var reader = h.NewReader();
        var pending = reader.Cache.GetOrCreateAsync("race", "HUF:2026-09", async () =>
        {
            var amount = await reader.Db.Transactions.Select(item => item.Amount).SingleAsync();
            started.SetResult();
            await finish.Task;
            return amount;
        }, Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        transaction.Amount = 200;
        await h.Db.SaveChangesAsync();
        finish.SetResult();
        Assert.Equal(100, await pending);
        Assert.Equal(200, await reader.Cache.GetOrCreateAsync("race", "HUF:2026-09", () => reader.Db.Transactions.Select(item => item.Amount).SingleAsync(), Token));
    }

    [FinancialCacheFact]
    public async Task BackupRestore_InvalidatesPreviouslyCachedTransactionsAndBalances()
    {
        await using var h = await CreateAsync();
        var transaction = await h.AddAsync(100);
        var imports = new ImportExportService(h.Db, h.Transactions, h.UserContext);
        var backup = await imports.ExportBackupAsync(Token);
        transaction.Amount = 200;
        await h.Db.SaveChangesAsync();
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        await h.Accounts.GetCurrentBalancesAsync(Token);
        await imports.RestoreAsync(backup, Token);
        Assert.Equal(100, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        Assert.Equal(900, (await h.Accounts.GetCurrentBalancesAsync(Token)).Single().Value);
    }

    [FinancialCacheFact]
    public async Task RecurringWorkerAndTemplateChanges_InvalidateTransactionsStatisticsAndBalances()
    {
        await using var h = await CreateAsync();
        var template = new RecurringTransaction { Id = Guid.NewGuid(), AccountId = h.Account.Id, CategoryId = h.Category.Id, Type = TransactionType.Expense, Amount = 100, Enabled = true, FirstOccurrence = new DateOnly(2026, 9, 8), AutomationStartsOn = new DateOnly(2026, 9, 8), Frequency = RecurringFrequency.Monthly };
        h.Db.RecurringTransactions.Add(template);
        await h.Db.SaveChangesAsync();
        Assert.Empty(await h.Transactions.GetForMonthAsync(2026, 9, Token));
        Assert.Equal(100, (await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token)).RecurringExpenses.Single().Amount);
        template.Amount = 200;
        await h.Db.SaveChangesAsync();
        Assert.Equal(200, (await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token)).RecurringExpenses.Single().Amount);
        var services = new ServiceCollection().AddScoped(_ => new DbContextOptionsBuilder<PocketLedgerDbContext>().UseNpgsql(fixture.ConnectionString).Options).AddRecurringTransactionProcessingDataAccess();
        await using var provider = services.BuildServiceProvider();
        using var worker = new RecurringTransactionWorker(provider.GetRequiredService<IServiceScopeFactory>(), h.Clock, new UserDateProvider(h.Clock), Options.Create(new UserDateOptions()), NullLogger<RecurringTransactionWorker>.Instance);
        await worker.ProcessDueOccurrencesAsync(Token);
        Assert.Equal(200, (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        Assert.Equal(800, (await h.Accounts.GetCurrentBalancesAsync(Token))[h.Account.Id]);
        Assert.Equal(200, (await h.Statistics.GetSummaryAsync(2026, 9, "HUF", Token)).Expenses);
        await worker.ProcessDueOccurrencesAsync(Token);
        Assert.Single(await h.Transactions.GetForMonthAsync(2026, 9, Token));
    }

    [FinancialCacheFact]
    public async Task DebtMetadataChanges_InvalidateTransactionNavigationData()
    {
        await using var h = await CreateAsync();
        var debt = new Debt { Id = Guid.NewGuid(), Name = "Loan", CounterpartyName = "Bank", Type = DebtType.Bank, Direction = DebtDirection.Payable, Currency = "HUF", OriginalAmount = 1000, StartDate = new DateOnly(2026, 9, 1), AccountId = h.Account.Id };
        h.Db.Debts.Add(debt);
        await h.Db.SaveChangesAsync();
        var transaction = await h.AddAsync(100);
        transaction.DebtId = debt.Id;
        transaction.DebtOperationType = DebtOperationType.Payment;
        await h.Db.SaveChangesAsync();
        Assert.Equal("Loan", (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Debt!.Name);
        debt.Name = "Renamed loan";
        await h.Db.SaveChangesAsync();
        Assert.Equal("Renamed loan", (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Debt!.Name);
        h.Counter.Reads = 0;
        Assert.Equal("Renamed loan", (await h.Transactions.GetForMonthAsync(2026, 9, Token)).Single().Debt!.Name);
        Assert.Equal(0, h.Counter.Reads);
    }

    [FinancialCacheFact]
    public async Task UnreachableValkey_RealClientFallsBackAndDisabledCacheIsNotRegistered()
    {
        await using var h = await CreateAsync();
        await h.AddAsync(100);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FinancialCache:ConnectionString"] = "127.0.0.1:1" }).Build();
        await using var provider = new ServiceCollection().AddLogging().AddFinancialCache(configuration).BuildServiceProvider();
        var cache = new FinancialCache(h.Db, new TestUser(h.OwnerId), h.UserContext, provider.GetRequiredService<IDistributedCache>(), Options.Create(new FinancialCacheOptions { ConnectionString = "127.0.0.1:1" }), NullLogger<FinancialCache>.Instance);
        var service = new TransactionService(h.Db, h.UserContext, cache);
        Assert.Equal(100, (await service.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        Assert.Equal(100, (await service.GetForMonthAsync(2026, 9, Token)).Single().Amount);
        await using var disabled = new ServiceCollection().AddFinancialCache(new ConfigurationBuilder().Build()).BuildServiceProvider();
        Assert.Null(disabled.GetService<FinancialCache>());
        Assert.Null(disabled.GetService<IDistributedCache>());
    }

    [FinancialCacheFact]
    public async Task EvictionRefillsAndCancellationIsNotSwallowed()
    {
        await using var h = await CreateAsync();
        await h.AddAsync(100);
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        await h.Store.RemoveAsync(h.Store.LastKey!);
        h.Counter.Reads = 0;
        Assert.Single(await h.Transactions.GetForMonthAsync(2026, 9, Token));
        Assert.True(h.Counter.Reads > 0);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Transactions.GetForMonthAsync(2026, 9, cancelled.Token));
    }

    [FinancialCacheFact]
    public async Task Migration_CanBeRolledBackAndReappliedWithoutChangingTheEfModel()
    {
        await using var h = await CreateAsync();
        Assert.False(h.Db.Database.HasPendingModelChanges());
        await h.Db.GetService<IMigrator>().MigrateAsync("20260903085115_RemoveBudapestRecurringDateDefault");
        await h.Db.Database.MigrateAsync();
        await h.AddAsync(100);
        await h.Transactions.GetForMonthAsync(2026, 9, Token);
        h.Counter.Reads = 0;
        Assert.Single(await h.Transactions.GetForMonthAsync(2026, 9, Token));
        Assert.Equal(0, h.Counter.Reads);
    }

    private async Task<Harness> CreateAsync()
    {
        var h = new Harness(fixture, Guid.NewGuid());
        h.Db.UserPreferences.Add(new UserPreference { UserId = h.OwnerId, TimeZoneId = "Europe/Budapest" });
        h.Db.Accounts.Add(h.Account);
        h.Db.Categories.Add(h.Category);
        await h.Db.SaveChangesAsync();
        await h.UserContext.GetUserAsync();
        return h;
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly FinancialCacheFixture fixture;
        public Guid OwnerId { get; }
        public QueryCounter Counter { get; } = new();
        public FixedClock Clock { get; } = new();
        public PocketLedgerDbContext Db { get; }
        public FailingStore Store { get; }
        public UserContextService UserContext { get; }
        public FinancialCache Cache { get; }
        public TransactionService Transactions { get; }
        public AccountService Accounts { get; }
        public StatisticsService Statistics { get; }
        public Account Account { get; } = new() { Id = Guid.NewGuid(), Name = "Cash", Type = AccountType.Cash, Currency = "HUF", InitialBalance = 1000, IncludeInMainBalance = true };
        public Category Category { get; } = new() { Id = Guid.NewGuid(), Name = "Food", Type = CategoryType.Expense };

        public Harness(FinancialCacheFixture fixture, Guid ownerId)
        {
            this.fixture = fixture;
            OwnerId = ownerId;
            var user = new TestUser(ownerId);
            Db = new PocketLedgerDbContext(new DbContextOptionsBuilder<PocketLedgerDbContext>().UseNpgsql(fixture.ConnectionString).AddInterceptors(Counter).Options, user);
            var dates = new UserDateProvider(Clock);
            UserContext = new UserContextService(user, Db, dates, Options.Create(new UserDateOptions()));
            Store = new FailingStore(fixture.Store);
            Cache = new FinancialCache(Db, user, UserContext, Store, Options.Create(new FinancialCacheOptions { ConnectionString = Environment.GetEnvironmentVariable("PL_TEST_VALKEY") }), NullLogger<FinancialCache>.Instance);
            Transactions = new TransactionService(Db, UserContext, Cache);
            Accounts = new AccountService(Db, Clock, UserContext, dates, Cache);
            Statistics = new StatisticsService(Db, Accounts, Cache);
        }

        public Harness NewReader() => new(fixture, OwnerId);
        public async Task<Transaction> AddAsync(decimal amount, string? note = null)
        {
            var transaction = new Transaction { Id = Guid.NewGuid(), AccountId = Account.Id, CategoryId = Category.Id, Type = TransactionType.Expense, Amount = amount, SourceCurrency = "HUF", TransactionDate = new DateOnly(2026, 9, 8), OccurredAtUtc = Clock.Now, Note = note };
            Db.Transactions.Add(transaction);
            await Db.SaveChangesAsync();
            return transaction;
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestUser(Guid id) : ICurrentUser
    {
        public Guid UserId => id;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("SELECT") && !command.CommandText.Contains("financial_cache_revisions") && !command.CommandText.Contains("user_preferences")) Reads++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailingStore(IDistributedCache inner) : IDistributedCache
    {
        public string? LastKey { get; private set; }
        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            LastKey = key;
            return FailReads ? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated outage") : inner.GetAsync(key, token);
        }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => FailWrites ? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Simulated outage") : inner.SetAsync(key, value, options, token);
        public byte[]? Get(string key) => throw new NotSupportedException();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new NotSupportedException();
        public void Refresh(string key) => throw new NotSupportedException();
        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);
        public void Remove(string key) => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }
}
