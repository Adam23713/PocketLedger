using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class UserIsolationTests
{
    [Fact]
    public void CrossTenantConstructor_IsNotAvailableToExternalSubclasses()
    {
        var constructor = typeof(PocketLedgerDbContext).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(item => item.GetParameters() is [{ ParameterType: var optionsType }, { ParameterType: var accessType }]
                && optionsType == typeof(DbContextOptions<PocketLedgerDbContext>) && accessType == typeof(bool));

        Assert.True(constructor.IsFamilyAndAssembly);
        Assert.False(constructor.IsFamily);
        Assert.False(constructor.IsFamilyOrAssembly);
    }

    [Fact]
    public async Task QueryFilters_ReturnOnlyCurrentUsersFinancialData()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            seed.Accounts.AddRange(CreateAccount(owner, "Mine"), CreateAccount(other, "Other"));
            seed.Categories.AddRange(CreateCategory(owner, "Mine"), CreateCategory(other, "Other"));
            seed.Debts.AddRange(CreateDebt(owner, "Mine"), CreateDebt(other, "Other"));
            seed.Transactions.AddRange(CreateTransaction(owner), CreateTransaction(other));
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));

        Assert.Equal(owner, Assert.Single(await db.Accounts.ToListAsync()).OwnerId);
        Assert.Equal(owner, Assert.Single(await db.Categories.ToListAsync()).OwnerId);
        Assert.Equal(owner, Assert.Single(await db.Debts.ToListAsync()).OwnerId);
        Assert.Equal(owner, Assert.Single(await db.Transactions.ToListAsync()).OwnerId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryFilters_MissingOrUnauthenticatedContext_ReturnsNoFinancialData(bool provideUnauthenticatedContext)
    {
        var owner = Guid.NewGuid();
        var options = CreateOptions();
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            seed.Accounts.Add(CreateAccount(owner, "Account"));
            seed.Categories.Add(CreateCategory(owner, "Category"));
            seed.Debts.Add(CreateDebt(owner, "Debt"));
            seed.Transactions.Add(CreateTransaction(owner));
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, provideUnauthenticatedContext ? new UnauthenticatedCurrentUser() : null);

        Assert.Empty(await db.Accounts.ToListAsync());
        Assert.Empty(await db.Categories.ToListAsync());
        Assert.Empty(await db.Debts.ToListAsync());
        Assert.Empty(await db.Transactions.ToListAsync());
        Assert.Empty(await db.RecurringTransactions.ToListAsync());
        Assert.Empty(await db.RecurringTransactionOccurrences.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_MissingOrUnauthenticatedContext_RejectsFinancialWrites(bool provideUnauthenticatedContext)
    {
        await using var db = new PocketLedgerDbContext(CreateOptions(), provideUnauthenticatedContext ? new UnauthenticatedCurrentUser() : null);
        db.Accounts.Add(CreateAccount(Guid.NewGuid(), "Rejected"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_AssignsCurrentOwnerToNewEntities()
    {
        var owner = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));
        var account = CreateAccount(Guid.Empty, "New");
        db.Accounts.Add(account);

        await db.SaveChangesAsync();

        Assert.Equal(owner, account.OwnerId);
    }

    [Fact]
    public async Task SaveChanges_RejectsModificationOfAnotherOwnersEntity()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var options = CreateOptions();
        var account = CreateAccount(other, "Other");
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            seed.Accounts.Add(account);
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));
        account.Name = "Changed";
        db.Attach(account).State = EntityState.Modified;

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Category")]
    [InlineData("Debt")]
    [InlineData("Transaction")]
    [InlineData("RecurringTransaction")]
    [InlineData("RecurringTransactionOccurrence")]
    public async Task SaveChanges_RejectsForgedUpdateWithCurrentOwnerId(string entityType)
    {
        var currentOwner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var options = CreateOptions();
        var ids = await SeedFinanceGraphAsync(options, otherOwner);
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(currentOwner));
        db.Update(CreateForgedEntity(entityType, ids, currentOwner));

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("Account")]
    [InlineData("Category")]
    [InlineData("Debt")]
    [InlineData("Transaction")]
    [InlineData("RecurringTransaction")]
    [InlineData("RecurringTransactionOccurrence")]
    public async Task SaveChanges_RejectsForgedDeleteWithCurrentOwnerId(string entityType)
    {
        var currentOwner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var options = CreateOptions();
        var ids = await SeedFinanceGraphAsync(options, otherOwner);
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(currentOwner));
        db.Remove(CreateForgedEntity(entityType, ids, currentOwner));

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_RejectsReferenceToAnotherOwnersFinancialEntity()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var options = CreateOptions();
        var otherAccount = CreateAccount(other, "Other");
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            seed.Accounts.Add(otherAccount);
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), AccountId = otherAccount.Id, Type = TransactionType.Expense, Amount = 10, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" });

        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_RejectsCategoryAndDebtReferencesOwnedByAnotherUser()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var options = CreateOptions();
        var otherCategory = CreateCategory(other, "Other category");
        var otherDebt = CreateDebt(other, "Other debt");
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            seed.AddRange(otherCategory, otherDebt);
            await seed.SaveChangesAsync();
        }

        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(owner));
        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), CategoryId = otherCategory.Id, Type = TransactionType.Expense, Amount = 10, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" });
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();

        db.Transactions.Add(new Transaction { Id = Guid.NewGuid(), DebtId = otherDebt.Id, Type = TransactionType.DebtEntry, Amount = 10, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" });
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_RejectsAllCrossOwnerFinanceReferences()
    {
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var options = CreateOptions();
        var ids = await SeedFinanceGraphAsync(options, other);

        await AssertReferenceRejectedAsync(options, owner, new Transaction { Id = Guid.NewGuid(), TargetAccountId = ids.AccountId, Type = TransactionType.Transfer, Amount = 10, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" });
        await AssertReferenceRejectedAsync(options, owner, new Category { Id = Guid.NewGuid(), ParentCategoryId = ids.CategoryId, Name = "Child", Type = CategoryType.Expense });
        await AssertReferenceRejectedAsync(options, owner, new Debt { Id = Guid.NewGuid(), AccountId = ids.AccountId, Name = "Debt", CounterpartyName = "Test", Currency = "HUF", OriginalAmount = 10, StartDate = new DateOnly(2026, 1, 1) });
        await AssertReferenceRejectedAsync(options, owner, new RecurringTransaction { Id = Guid.NewGuid(), AccountId = ids.AccountId, Type = TransactionType.Adjustment, Amount = 10, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly });
        await AssertReferenceRejectedAsync(options, owner, new RecurringTransaction { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), CategoryId = ids.CategoryId, Type = TransactionType.Expense, Amount = 10, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly }, createOwnedAccount: true);
        await AssertReferenceRejectedAsync(options, owner, new RecurringTransaction { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), DebtId = ids.DebtId, Type = TransactionType.Expense, Amount = 10, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly }, createOwnedAccount: true);
        await AssertReferenceRejectedAsync(options, owner, new RecurringTransactionOccurrence { Id = Guid.NewGuid(), RecurringTransactionId = ids.RecurringTransactionId, OccurrenceDate = new DateOnly(2026, 1, 1) });
        await AssertReferenceRejectedAsync(options, owner, new RecurringTransactionOccurrence { Id = Guid.NewGuid(), RecurringTransactionId = Guid.NewGuid(), TransactionId = ids.TransactionId, OccurrenceDate = new DateOnly(2026, 1, 1) }, createOwnedTemplate: true);
    }

    [Fact]
    public async Task RecurringWorker_UsesExplicitCrossTenantContextForAllOwners()
    {
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using (var seed = new CrossTenantPocketLedgerDbContext(options))
        {
            AddRecurringTemplate(seed, firstOwner, "First");
            AddRecurringTemplate(seed, secondOwner, "Second");
            await seed.SaveChangesAsync();
        }
        var services = new ServiceCollection()
            .AddSingleton(options)
            .AddRecurringTransactionProcessingDataAccess()
            .BuildServiceProvider();
        var worker = new RecurringTransactionWorker(services.GetRequiredService<IServiceScopeFactory>(), new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)), NullLogger<RecurringTransactionWorker>.Instance);

        await worker.ProcessDueOccurrencesAsync(CancellationToken.None);

        await using var verify = new CrossTenantPocketLedgerDbContext(options);
        var transactions = await verify.Transactions.OrderBy(item => item.OwnerId).ToListAsync();
        Assert.Equal(2, transactions.Count);
        Assert.Contains(transactions, item => item.OwnerId == firstOwner);
        Assert.Contains(transactions, item => item.OwnerId == secondOwner);
    }

    private static void AddRecurringTemplate(PocketLedgerDbContext db, Guid ownerId, string name)
    {
        var account = CreateAccount(ownerId, name);
        db.Accounts.Add(account);
        db.RecurringTransactions.Add(new RecurringTransaction
        {
            Id = Guid.NewGuid(), OwnerId = ownerId, Type = TransactionType.Adjustment, AccountId = account.Id, Account = account, Amount = 10,
            AdjustmentDirection = AdjustmentDirection.Increase, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly, Enabled = true
        });
    }

    private static async Task AssertReferenceRejectedAsync(DbContextOptions<PocketLedgerDbContext> options, Guid ownerId, object entity, bool createOwnedAccount = false, bool createOwnedTemplate = false)
    {
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        if (createOwnedAccount && entity is RecurringTransaction template)
        {
            var account = CreateAccount(ownerId, "Owned");
            account.Id = template.AccountId;
            db.Accounts.Add(account);
        }
        if (createOwnedTemplate && entity is RecurringTransactionOccurrence occurrence)
        {
            var account = CreateAccount(ownerId, "Owned");
            var ownedTemplate = new RecurringTransaction { Id = occurrence.RecurringTransactionId, OwnerId = ownerId, AccountId = account.Id, Account = account, Type = TransactionType.Adjustment, Amount = 10, AdjustmentDirection = AdjustmentDirection.Increase, FirstOccurrence = occurrence.OccurrenceDate, AutomationStartsOn = occurrence.OccurrenceDate, Frequency = RecurringFrequency.Monthly };
            db.AddRange(account, ownedTemplate);
        }
        db.Add(entity);
        await Assert.ThrowsAsync<BusinessRuleException>(() => db.SaveChangesAsync());
    }

    private static async Task<FinanceGraphIds> SeedFinanceGraphAsync(DbContextOptions<PocketLedgerDbContext> options, Guid ownerId)
    {
        await using var db = new CrossTenantPocketLedgerDbContext(options);
        var account = CreateAccount(ownerId, "Account");
        var category = CreateCategory(ownerId, "Category");
        var debt = CreateDebt(ownerId, "Debt");
        var transaction = CreateTransaction(ownerId);
        var template = new RecurringTransaction { Id = Guid.NewGuid(), OwnerId = ownerId, AccountId = account.Id, Account = account, Type = TransactionType.Adjustment, Amount = 10, AdjustmentDirection = AdjustmentDirection.Increase, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly };
        var occurrence = new RecurringTransactionOccurrence { Id = Guid.NewGuid(), OwnerId = ownerId, RecurringTransactionId = template.Id, RecurringTransaction = template, TransactionId = transaction.Id, Transaction = transaction, OccurrenceDate = new DateOnly(2026, 1, 1) };
        db.AddRange(account, category, debt, transaction, template, occurrence);
        await db.SaveChangesAsync();
        return new FinanceGraphIds(account.Id, category.Id, debt.Id, transaction.Id, template.Id, occurrence.Id);
    }

    private static object CreateForgedEntity(string entityType, FinanceGraphIds ids, Guid ownerId) => entityType switch
    {
        "Account" => CreateForgedAccount(ids.AccountId, ownerId),
        "Category" => new Category { Id = ids.CategoryId, OwnerId = ownerId, Name = "Forged", Type = CategoryType.Expense },
        "Debt" => new Debt { Id = ids.DebtId, OwnerId = ownerId, Name = "Forged", CounterpartyName = "Test", Currency = "HUF", OriginalAmount = 10, StartDate = new DateOnly(2026, 1, 1) },
        "Transaction" => new Transaction { Id = ids.TransactionId, OwnerId = ownerId, Type = TransactionType.Adjustment, Amount = 10, AdjustmentDirection = AdjustmentDirection.Increase, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" },
        "RecurringTransaction" => new RecurringTransaction { Id = ids.RecurringTransactionId, OwnerId = ownerId, AccountId = ids.AccountId, Type = TransactionType.Adjustment, Amount = 10, AdjustmentDirection = AdjustmentDirection.Increase, FirstOccurrence = new DateOnly(2026, 1, 1), AutomationStartsOn = new DateOnly(2026, 1, 1), Frequency = RecurringFrequency.Monthly },
        "RecurringTransactionOccurrence" => new RecurringTransactionOccurrence { Id = ids.OccurrenceId, OwnerId = ownerId, RecurringTransactionId = ids.RecurringTransactionId, TransactionId = ids.TransactionId, OccurrenceDate = new DateOnly(2026, 1, 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    private static Account CreateForgedAccount(Guid id, Guid ownerId)
    {
        var account = CreateAccount(ownerId, "Forged");
        account.Id = id;
        return account;
    }

    private sealed record FinanceGraphIds(Guid AccountId, Guid CategoryId, Guid DebtId, Guid TransactionId, Guid RecurringTransactionId, Guid OccurrenceId);

    private static DbContextOptions<PocketLedgerDbContext> CreateOptions() => new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static Account CreateAccount(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, Type = AccountType.Cash, Currency = "HUF" };
    private static Category CreateCategory(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, Type = CategoryType.Expense, Icon = "food-1" };
    private static Debt CreateDebt(Guid owner, string name) => new() { Id = Guid.NewGuid(), OwnerId = owner, Name = name, CounterpartyName = "Test", Currency = "HUF", OriginalAmount = 100, StartDate = new DateOnly(2026, 1, 1) };
    private static Transaction CreateTransaction(Guid owner) => new() { Id = Guid.NewGuid(), OwnerId = owner, Type = TransactionType.Adjustment, Amount = 100, AdjustmentDirection = AdjustmentDirection.Increase, TransactionDate = new DateOnly(2026, 1, 1), SourceCurrency = "HUF" };

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
        public bool IsAuthenticated => true;
    }

    private sealed class UnauthenticatedCurrentUser : ICurrentUser
    {
        public Guid UserId => throw new InvalidOperationException("UserId must not be evaluated for an unauthenticated context.");
        public bool IsAuthenticated => false;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
