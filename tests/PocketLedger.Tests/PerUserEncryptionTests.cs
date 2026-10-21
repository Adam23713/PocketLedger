using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Security;
using PocketLedger.Services;
using Npgsql;

namespace PocketLedger.Tests;

public sealed class PerUserEncryptionTests
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly DatabaseEncryption encryption = new(new EphemeralDataProtectionProvider());
    private readonly DbContextOptions<PocketLedgerDbContext> options = new DbContextOptionsBuilder<PocketLedgerDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;

    // The in-memory provider snapshots rows with the converters of the first model that touched them, so checks that need
    // a second model (raw ciphertext, the cross-tenant worker) run against PostgreSQL in PerUserEncryptionPostgresTests.
    [Fact]
    public async Task UserFields_ReadBackWithTheUsersKey()
    {
        using var key = UserDataKey.Generate(owner);
        var accountId = await SeedAsync(key, "Main bank", "Rent for the flat");

        using (UserKeyContext.Enter(Clone(key)))
        await using (var db = Tenant())
        {
            Assert.Equal("Main bank", (await db.Accounts.SingleAsync(item => item.Id == accountId)).Name);
            Assert.Equal("Rent for the flat", (await db.RecurringTransactions.SingleAsync()).Note);
        }
    }

    [Fact]
    public async Task UserFields_CannotBeReadWithoutTheKeyOrWithAnotherUsersKey()
    {
        using var key = UserDataKey.Generate(owner);
        await SeedAsync(key, "Main bank", null);

        await using (var db = Tenant())
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => db.Accounts.ToListAsync());
            Assert.True(HasInner<UserKeyUnavailableException>(exception), exception.ToString());
        }

        using (UserKeyContext.Enter(UserDataKey.Generate(Guid.NewGuid())))
        await using (var db = Tenant())
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => db.Accounts.ToListAsync());
            Assert.True(HasInner<UserKeyException>(exception), exception.ToString());
        }
    }

    [Fact]
    public async Task WritingUserFieldsWithoutTheKey_IsRefused()
    {
        await using var db = Tenant();
        db.Accounts.Add(new Account { Id = Guid.NewGuid(), Name = "No key", Type = AccountType.Cash, Currency = "HUF" });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.True(HasInner<UserKeyUnavailableException>(exception), exception.ToString());
    }

    [Fact]
    public void CachePayloads_AreSealedWithTheUsersKeyUnderTheHostLayer()
    {
        using var key = UserDataKey.Generate(owner);
        var protector = new EphemeralDataProtectionProvider().CreateProtector("cache-test");
        var payload = "{\"note\":\"private\"}"u8.ToArray();

        var sealedBytes = FinancialCache.Seal(protector, key, "Cache.GetForMonthAsync", payload);

        Assert.DoesNotContain("private", System.Text.Encoding.UTF8.GetString(protector.Unprotect(sealedBytes)));
        Assert.Equal(payload, FinancialCache.Open(protector, key, "Cache.GetForMonthAsync", sealedBytes));
        using var otherUser = UserDataKey.Generate(Guid.NewGuid());
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => FinancialCache.Open(protector, otherUser, "Cache.GetForMonthAsync", sealedBytes));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => FinancialCache.Open(protector, key, "Cache.GetSummaryAsync", sealedBytes));
    }

    internal static async Task<Guid> SeedAsync(Func<PocketLedgerDbContext> tenant, UserDataKey key, string accountName, string? recurringNote)
    {
        using var scope = UserKeyContext.Enter(Clone(key));
        await using var db = tenant();
        var account = new Account { Id = Guid.NewGuid(), Name = accountName, Type = AccountType.Cash, Currency = "HUF" };
        db.Accounts.Add(account);
        db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = Guid.NewGuid(), Type = TransactionType.Adjustment, AccountId = account.Id, Amount = 10, AdjustmentDirection = AdjustmentDirection.Increase,
            FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly, Enabled = true, Note = recurringNote
        });
        await db.SaveChangesAsync();
        return account.Id;
    }

    private Task<Guid> SeedAsync(UserDataKey key, string accountName, string? recurringNote) => SeedAsync(Tenant, key, accountName, recurringNote);

    private PocketLedgerDbContext Tenant() => new(options, new TestCurrentUser(owner), encryption: encryption);

    // UserKeyContext owns and clears the keys it is given, so every scope gets its own copy.
    internal static UserDataKey Clone(UserDataKey key)
    {
        var wrappingKey = EphemeralKeyWrap.GenerateWrappingKey();
        return EphemeralKeyWrap.Unwrap(EphemeralKeyWrap.Wrap(key, wrappingKey, EphemeralKeyPurpose.Session, "test-copy"), wrappingKey, EphemeralKeyPurpose.Session, key.UserId, "test-copy");
    }

    internal static bool HasInner<T>(Exception? exception) where T : Exception
    {
        for (; exception is not null; exception = exception.InnerException)
            if (exception is T) return true;
        return false;
    }

    internal sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }

    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PL_TEST_POSTGRES"))) Skip = "Set PL_TEST_POSTGRES to run PostgreSQL integration tests.";
    }
}

public sealed class PerUserEncryptionPostgresTests : IAsyncLifetime
{
    private readonly Guid owner = Guid.NewGuid();
    private readonly DatabaseEncryption encryption = new(new EphemeralDataProtectionProvider());
    private readonly string databaseName = "pl_user_keys_" + Guid.NewGuid().ToString("N");
    private string connectionString = "";
    private DbContextOptions<PocketLedgerDbContext> options = null!;

    public async Task InitializeAsync()
    {
        var postgres = Environment.GetEnvironmentVariable("PL_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres)) return;
        await using var admin = new NpgsqlConnection(postgres);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {databaseName}", admin)) await create.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres) { Database = databaseName }.ConnectionString;
        options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseNpgsql(connectionString).Options;
    }

    public async Task DisposeAsync()
    {
        if (connectionString.Length == 0) return;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("PL_TEST_POSTGRES"));
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE {databaseName} WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task StoredColumns_HoldTheUsersCiphertextUnderTheHostLayer()
    {
        await MigrateAsync();
        using var key = UserDataKey.Generate(owner);
        await PerUserEncryptionTests.SeedAsync(Tenant, key, "Main bank", "Monthly rent");

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT a.name, r.note FROM accounts a JOIN recurring_transactions r ON r.account_id = a.id", connection);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.DoesNotContain("Main bank", reader.GetString(0));
            Assert.DoesNotContain("rent", reader.GetString(1), StringComparison.OrdinalIgnoreCase);
        }

        // Without the user's key the host layer opens only to the per-user ciphertext, which the user's key then opens.
        await using var crossTenant = new CrossTenantPocketLedgerDbContext(options, encryption);
        var inner = (await crossTenant.Accounts.SingleAsync()).Name;
        Assert.DoesNotContain("Main bank", inner);
        Assert.Equal("Main bank", UserFieldCipher.DecryptString(inner, key, "AccountName"));
    }

    [PostgresFact]
    public async Task RecurringWorker_CopiesTheTemplateNoteWithoutBeingAbleToReadIt()
    {
        await MigrateAsync();
        using var key = UserDataKey.Generate(owner);
        await PerUserEncryptionTests.SeedAsync(Tenant, key, "Main bank", "Monthly rent");
        await using var services = new ServiceCollection().AddSingleton(options).AddSingleton(encryption).AddRecurringTransactionProcessingDataAccess().BuildServiceProvider();
        var clock = new PerUserEncryptionTests.FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var worker = new RecurringTransactionWorker(services.GetRequiredService<IServiceScopeFactory>(), clock, new UserDateProvider(clock), Options.Create(new UserDateOptions()), NullLogger<RecurringTransactionWorker>.Instance);

        await worker.ProcessDueOccurrencesAsync(CancellationToken.None);

        using (UserKeyContext.Enter(PerUserEncryptionTests.Clone(key)))
        await using (var db = Tenant())
            Assert.Equal("Monthly rent", (await db.Transactions.SingleAsync()).Note);
    }

    [PostgresFact]
    public async Task Migration_RefusesAPopulatedFinanceDatabase()
    {
        await using (var db = new PocketLedgerDbContext(options))
            await db.GetService<IMigrator>().MigrateAsync("20261019120000_AddBlockedUsers");
        await using (var db = new CrossTenantPocketLedgerDbContext(options))
        {
            db.Accounts.Add(new Account { Id = Guid.NewGuid(), OwnerId = owner, Name = "Legacy", Type = AccountType.Cash, Currency = "HUF" });
            await db.SaveChangesAsync();
        }

        await using var migrating = new PocketLedgerDbContext(options);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => migrating.Database.MigrateAsync());
        Assert.Contains("Per-user encryption requires an empty finance database", exception.MessageText);
    }

    private async Task MigrateAsync()
    {
        await using var db = new PocketLedgerDbContext(options);
        await db.Database.MigrateAsync();
    }

    private PocketLedgerDbContext Tenant() => new(options, new PerUserEncryptionTests.TestCurrentUser(owner), encryption: encryption);
}
