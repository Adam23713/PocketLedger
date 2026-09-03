using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class BackupSemanticValidationTests
{
    [Fact]
    public void ValidFullBackup_PassesSemanticValidation()
    {
        Assert.Empty(BackupValidator.Validate(ValidBackup()));
    }

    [Fact]
    public void UnknownCurrency_IdentifiesAccountAndCurrencyRule()
    {
        var backup = ValidBackup(); var account = backup.Accounts[0];
        var errors = BackupValidator.Validate(backup with { Accounts = [account with { Currency = "XYZ" }, .. backup.Accounts.Skip(1)] });
        Assert.Contains(errors, error => error.Contains(account.Id.ToString()) && error.Contains("rule 'currency'"));
    }

    [Fact]
    public void AccountTransactionCurrencyMismatch_IsRejected()
    {
        var backup = ValidBackup(); var transaction = backup.Transactions[0];
        AssertRule(BackupValidator.Validate(backup with { Transactions = [transaction with { SourceCurrency = "EUR" }] }), transaction.Id, "currency-consistency");
    }

    [Fact]
    public void WrongCategoryType_IsRejectedBySharedTransactionRules()
    {
        var backup = ValidBackup(); var transaction = backup.Transactions[0]; var category = backup.Categories[0];
        AssertRule(BackupValidator.Validate(backup with { Categories = [category with { Type = CategoryType.Income }] }), transaction.Id, "transaction");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InvalidTransferAccounts_AreRejected(bool sameAccount, bool missingTarget)
    {
        var backup = ValidBackup(); var source = backup.Accounts[0]; var target = backup.Accounts[1];
        var transaction = backup.Transactions[0] with { Type = TransactionType.Transfer, CategoryId = null, TargetAccountId = missingTarget ? null : sameAccount ? source.Id : target.Id, TargetAmount = 10, ExchangeRate = 1, TargetCurrency = missingTarget ? null : sameAccount ? source.Currency : target.Currency };
        AssertRule(BackupValidator.Validate(backup with { Transactions = [transaction] }), transaction.Id, "transfer");
    }

    [Fact]
    public void InvalidDebtOperation_IsRejected()
    {
        var backup = ValidBackup(); var debt = backup.Debts![0]; var transaction = backup.Transactions[0] with { Type = TransactionType.Income, CategoryId = null, DebtId = debt.Id, DebtOperationType = DebtOperationType.Payment };
        AssertRule(BackupValidator.Validate(backup with { Transactions = [transaction] }), transaction.Id, "debt-operation");
    }

    [Fact]
    public void InvalidRecurringTemplate_IsRejected()
    {
        var backup = ValidBackup(); var recurring = backup.RecurringTransactions[0] with { LastOccurrence = new DateOnly(2025, 1, 1) };
        AssertRule(BackupValidator.Validate(backup with { RecurringTransactions = [recurring] }), recurring.Id, "recurring-transaction");
    }

    [Fact]
    public void MissingReference_IdentifiesRecordAndReferenceRule()
    {
        var backup = ValidBackup(); var transaction = backup.Transactions[0] with { AccountId = Guid.NewGuid() };
        AssertRule(BackupValidator.Validate(backup with { Transactions = [transaction] }), transaction.Id, "source-account-reference");
    }

    [Fact]
    public void InvalidCategoryHierarchy_IsRejected()
    {
        var backup = ValidBackup();
        var parent = backup.Categories[0];
        var child = new CategoryBackup(Guid.NewGuid(), "Salary child", CategoryType.Income, null, parent.Id, 1);

        AssertRule(BackupValidator.Validate(backup with { Categories = [parent, child] }), child.Id, "category-type");
    }

    [Fact]
    public void InvalidDebtRecord_IsRejectedBySharedDebtRules()
    {
        var backup = ValidBackup();
        var debt = backup.Debts![0] with { OriginalAmount = 0 };

        AssertRule(BackupValidator.Validate(backup with { Debts = [debt] }), debt.Id, "debt");
    }

    [Fact]
    public void InvalidRecurringFrequency_IsRejected()
    {
        var backup = ValidBackup();
        var recurring = backup.RecurringTransactions[0] with { Frequency = (RecurringFrequency)999 };

        AssertRule(BackupValidator.Validate(backup with { RecurringTransactions = [recurring] }), recurring.Id, "recurring-transaction");
    }

    [Fact]
    public void DuplicateIds_AreReportedPerRecord()
    {
        var backup = ValidBackup();
        var account = backup.Accounts[0];

        AssertRule(BackupValidator.Validate(backup with { Accounts = [account, account] }), account.Id, "unique-id");
    }

    [Fact]
    public void ReferenceCannotResolveThroughDataOutsideBackupTenantGraph()
    {
        var backup = ValidBackup(); var otherOwnerAccountId = Guid.NewGuid(); var transaction = backup.Transactions[0] with { AccountId = otherOwnerAccountId };
        var errors = BackupValidator.Validate(backup with { Transactions = [transaction] });
        AssertRule(errors, transaction.Id, "source-account-reference");
    }

    [Fact]
    public void MultipleInvalidRecords_ReturnAllErrors()
    {
        var backup = ValidBackup(); var account = backup.Accounts[0]; var recurring = backup.RecurringTransactions[0] with { Frequency = (RecurringFrequency)999 };
        var errors = BackupValidator.Validate(backup with { Accounts = [account with { Currency = "XYZ" }, .. backup.Accounts.Skip(1)], RecurringTransactions = [recurring] });
        Assert.Contains(errors, error => error.Contains(account.Id.ToString())); Assert.Contains(errors, error => error.Contains(recurring.Id.ToString()));
    }

    [Fact]
    public async Task PreviewAndRestore_UseSameValidationAndInvalidRestoreDoesNotChangeData()
    {
        var ownerId = Guid.NewGuid(); var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        var existingId = Guid.NewGuid(); db.Accounts.Add(new Account { Id = existingId, Name = "Existing", Type = AccountType.Cash, Currency = "HUF" }); await db.SaveChangesAsync();
        var backup = ValidBackup(); var invalid = backup with { Accounts = [backup.Accounts[0] with { Currency = "XYZ" }, .. backup.Accounts.Skip(1)] }; var json = BackupJson.Serialize(invalid);
        var service = new ImportExportService(db, null!); var preview = service.PreviewRestore(json); var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RestoreAsync(json, CancellationToken.None));
        Assert.False(preview.IsValid); Assert.Equal(string.Join(" ", preview.Errors), exception.Message); Assert.Equal(existingId, (await db.Accounts.SingleAsync()).Id);
    }

    [Fact]
    public async Task ValidFullRestore_ReplacesDataAndAssignsCurrentOwnerToEveryRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var ownerId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        await CreateSqliteSchemaAsync(db);
        var existingId = Guid.NewGuid();
        db.Accounts.Add(new Account { Id = existingId, Name = "Existing", Type = AccountType.Cash, Currency = "HUF" });
        await db.SaveChangesAsync();

        await new ImportExportService(db, null!).RestoreAsync(BackupJson.Serialize(ValidBackup()), CancellationToken.None);

        Assert.DoesNotContain(await db.Accounts.AsNoTracking().ToListAsync(), item => item.Id == existingId);
        Assert.All(await db.Accounts.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.Categories.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.Transactions.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.RecurringTransactions.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.Debts.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
    }

    [Fact]
    public async Task Restore_RollsBackDeletionWhenWritingReplacementFails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new FailSecondSaveInterceptor();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options;
        var ownerId = Guid.NewGuid();
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(ownerId));
        await CreateSqliteSchemaAsync(db);
        var existingId = Guid.NewGuid();
        db.Accounts.Add(new Account { Id = existingId, Name = "Existing", Type = AccountType.Cash, Currency = "HUF" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        interceptor.Arm();

        var service = new ImportExportService(db, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(BackupJson.Serialize(ValidBackup()), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(existingId, (await db.Accounts.AsNoTracking().SingleAsync()).Id);
    }

    private static PocketLedgerBackup ValidBackup()
    {
        var huf = new AccountBackup(Guid.NewGuid(), "HUF account", AccountType.BankAccount, "HUF", 0, null, 0, true, true, true);
        var eur = new AccountBackup(Guid.NewGuid(), "EUR account", AccountType.BankAccount, "EUR", 0, null, 1, true, true, true);
        var expense = new CategoryBackup(Guid.NewGuid(), "Food", CategoryType.Expense, null, null, 0);
        var transaction = new TransactionBackup(Guid.NewGuid(), TransactionType.Expense, huf.Id, null, 10, null, null, new DateOnly(2026, 1, 2), expense.Id, null, SourceCurrency: "HUF");
        var recurring = new RecurringTransactionBackup(Guid.NewGuid(), TransactionType.Expense, huf.Id, expense.Id, 10, null, null, new DateOnly(2026, 2, 1), null, RecurringFrequency.Monthly, true);
        var debt = new DebtBackup(Guid.NewGuid(), "Loan", DebtDirection.Payable, DebtType.Bank, "Bank", 100, "HUF", new DateOnly(2026, 1, 1), null, null, DebtStatus.Active, null, huf.Id);
        return new PocketLedgerBackup(2, DateTimeOffset.UtcNow, [huf, eur], [expense], [transaction], [recurring], [debt]);
    }

    private static Task CreateSqliteSchemaAsync(PocketLedgerDbContext db)
    {
        var createScript = db.Database.GenerateCreateScript().Replace("(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date", "CURRENT_DATE", StringComparison.Ordinal);
        return db.Database.ExecuteSqlRawAsync(createScript);
    }

    private static void AssertRule(IReadOnlyList<string> errors, Guid id, string rule) => Assert.Contains(errors, error => error.Contains(id.ToString()) && error.Contains($"rule '{rule}'"));
    private sealed class TestCurrentUser(Guid userId) : ICurrentUser { public Guid UserId => userId; public bool IsAuthenticated => true; }

    private sealed class FailSecondSaveInterceptor : SaveChangesInterceptor
    {
        private int saveCount;
        private bool armed;

        public void Arm()
        {
            saveCount = 0;
            armed = true;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (armed && Interlocked.Increment(ref saveCount) == 2) throw new InvalidOperationException("Simulated replacement write failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
