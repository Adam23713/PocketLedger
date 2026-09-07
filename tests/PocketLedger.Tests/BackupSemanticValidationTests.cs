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

    [Theory]
    [InlineData("huf")]
    [InlineData(" HUF ")]
    public void NonCanonicalCurrency_IsRejectedBeforeRawValueCanBeRestored(string currency)
    {
        var backup = ValidBackup();
        var account = backup.Accounts[0];
        var transaction = backup.Transactions[0];
        var errors = BackupValidator.Validate(backup with
        {
            Accounts = [account with { Currency = currency }, .. backup.Accounts.Skip(1)],
            Transactions = [transaction with { SourceCurrency = currency }]
        });

        AssertRule(errors, account.Id, "currency");
        AssertRule(errors, transaction.Id, "source-currency");
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
    public void DebtPaymentCannotExceedRemainingBalanceAtItsPointInTheSequence()
    {
        var backup = ValidBackup();
        var debt = backup.Debts![0];
        var account = backup.Accounts[0];
        var overpayment = DebtOperation(debt.Id, account.Id, DebtOperationType.Payment, 120, new DateOnly(2026, 1, 2));
        var laterIncrease = DebtOperation(debt.Id, null, DebtOperationType.Increase, 50, new DateOnly(2026, 1, 3));

        AssertRule(BackupValidator.Validate(backup with { Transactions = [laterIncrease, overpayment] }), overpayment.Id, "debt-balance");
    }

    [Fact]
    public void MultipleDebtOperations_AreAppliedInDeterministicBusinessOrder()
    {
        var backup = ValidBackup();
        var debt = backup.Debts![0];
        var account = backup.Accounts[0];
        var firstPayment = DebtOperation(debt.Id, account.Id, DebtOperationType.Payment, 80, new DateOnly(2026, 1, 2));
        var increase = DebtOperation(debt.Id, null, DebtOperationType.Increase, 50, new DateOnly(2026, 1, 3));
        var finalPayment = DebtOperation(debt.Id, account.Id, DebtOperationType.Payment, 70, new DateOnly(2026, 1, 4));

        Assert.Empty(BackupValidator.Validate(backup with { Transactions = [finalPayment, increase, firstPayment] }));
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
    public async Task ReferenceCannotResolveThroughAnotherOwnersPersistedData()
    {
        var databaseName = Guid.NewGuid().ToString();
        var otherOwnerId = Guid.NewGuid();
        var otherOwnerAccountId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseInMemoryDatabase(databaseName).Options;
        await using (var otherOwnerDb = new PocketLedgerDbContext(options, new TestCurrentUser(otherOwnerId)))
        {
            otherOwnerDb.Accounts.Add(new Account { Id = otherOwnerAccountId, Name = "Other owner", Type = AccountType.Cash, Currency = "HUF" });
            await otherOwnerDb.SaveChangesAsync();
        }

        var backup = ValidBackup();
        var transaction = backup.Transactions[0] with { AccountId = otherOwnerAccountId };
        await using var currentOwnerDb = new PocketLedgerDbContext(options, new TestCurrentUser(Guid.NewGuid()));
        var preview = new ImportExportService(currentOwnerDb, null!, new TestUserContext()).PreviewRestore(BackupJson.Serialize(backup with { Transactions = [transaction] }));

        Assert.False(preview.IsValid);
        AssertRule(preview.Errors, transaction.Id, "source-account-reference");
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
        var service = new ImportExportService(db, null!, new TestUserContext()); var preview = service.PreviewRestore(json); var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => service.RestoreAsync(json, CancellationToken.None));
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

        var restoreDate = new DateOnly(2026, 9, 3);
        await new ImportExportService(db, null!, new TestUserContext(restoreDate)).RestoreAsync(BackupJson.Serialize(ValidBackup()), CancellationToken.None);

        Assert.DoesNotContain(await db.Accounts.AsNoTracking().ToListAsync(), item => item.Id == existingId);
        Assert.All(await db.Accounts.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.Categories.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.Transactions.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.RecurringTransactions.AsNoTracking().ToListAsync(), item => Assert.Equal(ownerId, item.OwnerId));
        Assert.All(await db.RecurringTransactions.AsNoTracking().ToListAsync(), item => Assert.Equal(restoreDate, item.AutomationStartsOn));
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

        var service = new ImportExportService(db, null!, new TestUserContext());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(BackupJson.Serialize(ValidBackup()), CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Equal(existingId, (await db.Accounts.AsNoTracking().SingleAsync()).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ExportedBackupCanReplaceSameOrDifferentOwnersData(bool differentOwner)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;
        var sourceOwner = Guid.NewGuid();
        await using var sourceDb = new PocketLedgerDbContext(options, new TestCurrentUser(sourceOwner));
        await CreateSqliteSchemaAsync(sourceDb);
        var sourceService = new ImportExportService(sourceDb, null!, new TestUserContext());
        await sourceService.RestoreAsync(BackupJson.Serialize(ValidBackup()), CancellationToken.None);
        var exported = BackupJson.Deserialize(await sourceService.ExportBackupAsync(CancellationToken.None));
        var destinationOwner = differentOwner ? Guid.NewGuid() : sourceOwner;
        await using var destinationDb = new PocketLedgerDbContext(options, new TestCurrentUser(destinationOwner));
        var destinationService = new ImportExportService(destinationDb, null!, new TestUserContext());
        var existingId = Guid.NewGuid();
        destinationDb.Accounts.Add(new Account { Id = existingId, Name = "Replaced", Type = AccountType.Cash, Currency = "HUF" });
        await destinationDb.SaveChangesAsync();

        await destinationService.RestoreAsync(BackupJson.Serialize(exported), CancellationToken.None);
        // A second restore must replace the first import, without accumulating duplicates.
        await destinationService.RestoreAsync(BackupJson.Serialize(exported), CancellationToken.None);
        var restored = BackupJson.Deserialize(await destinationService.ExportBackupAsync(CancellationToken.None));

        Assert.Equal(exported.Accounts.Count, restored.Accounts.Count);
        Assert.Equal(exported.Categories.Count, restored.Categories.Count);
        Assert.Equal(exported.Debts!.Count, restored.Debts!.Count);
        Assert.Equal(exported.Transactions.Count, restored.Transactions.Count);
        Assert.Equal(exported.RecurringTransactions.Count, restored.RecurringTransactions.Count);
        var originalIds = exported.Accounts.Select(item => item.Id).Concat(exported.Categories.Select(item => item.Id))
            .Concat(exported.Debts.Select(item => item.Id)).Concat(exported.Transactions.Select(item => item.Id)).Concat(exported.RecurringTransactions.Select(item => item.Id)).ToHashSet();
        var restoredIds = restored.Accounts.Select(item => item.Id).Concat(restored.Categories.Select(item => item.Id))
            .Concat(restored.Debts.Select(item => item.Id)).Concat(restored.Transactions.Select(item => item.Id)).Concat(restored.RecurringTransactions.Select(item => item.Id));
        Assert.All(restoredIds, id => Assert.DoesNotContain(id, originalIds));
        Assert.DoesNotContain(restored.Accounts, item => item.Id == existingId);
        Assert.All(await destinationDb.Accounts.AsNoTracking().ToListAsync(), item => Assert.Equal(destinationOwner, item.OwnerId));
        Assert.Empty(BackupValidator.Validate(restored));
        if (differentOwner)
        {
            var unchanged = BackupJson.Deserialize(await sourceService.ExportBackupAsync(CancellationToken.None));
            Assert.Equal(exported.Accounts.OrderBy(item => item.Id), unchanged.Accounts.OrderBy(item => item.Id));
            Assert.Equal(exported.Categories.OrderBy(item => item.Id), unchanged.Categories.OrderBy(item => item.Id));
            Assert.Equal(exported.Debts.OrderBy(item => item.Id), unchanged.Debts!.OrderBy(item => item.Id));
            Assert.Equal(exported.Transactions.OrderBy(item => item.Id), unchanged.Transactions.OrderBy(item => item.Id));
            Assert.Equal(exported.RecurringTransactions.OrderBy(item => item.Id), unchanged.RecurringTransactions.OrderBy(item => item.Id));
        }
    }

    [Fact]
    public async Task Restore_PreservesAllRelationshipsAndFinancialValuesWithNewIds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(Guid.NewGuid()));
        await CreateSqliteSchemaAsync(db);
        var backup = ValidBackup();
        backup = backup with { Accounts = [backup.Accounts[0] with { Color = "#123456", InitialBalance = 1234.50m }, backup.Accounts[1] with { Color = "#abcdef", InitialBalance = 12.34m }] };
        var huf = backup.Accounts[0]; var eur = backup.Accounts[1]; var parent = backup.Categories[0]; var debt = backup.Debts![0];
        var child = new CategoryBackup(Guid.NewGuid(), "Groceries", CategoryType.Expense, null, parent.Id, 1);
        var expense = backup.Transactions[0] with { CategoryId = child.Id, Note = "Groceries", TransactionTime = new TimeOnly(12, 30), OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 11, 30, 0, TimeSpan.Zero) };
        var transfer = new TransactionBackup(Guid.NewGuid(), TransactionType.Transfer, huf.Id, eur.Id, 400, 1, null, new DateOnly(2026, 1, 3), null, "Transfer", ExchangeRate: 0.0025m, SourceCurrency: "HUF", TargetCurrency: "EUR");
        var payment = DebtOperation(debt.Id, huf.Id, DebtOperationType.Payment, 20, new DateOnly(2026, 1, 4));
        var increase = DebtOperation(debt.Id, null, DebtOperationType.Increase, 30, new DateOnly(2026, 1, 5));
        var recurring = backup.RecurringTransactions[0] with { CategoryId = child.Id };
        var recurringPayment = recurring with { Id = Guid.NewGuid(), CategoryId = null, DebtId = debt.Id, DebtOperationType = DebtOperationType.Payment };
        var unlinkedDebt = debt with { Id = Guid.NewGuid(), Name = "Unlinked", AccountId = null };
        backup = backup with { Categories = [parent, child], Transactions = [expense, transfer, payment, increase], RecurringTransactions = [recurring, recurringPayment], Debts = [debt, unlinkedDebt] };
        var service = new ImportExportService(db, null!, new TestUserContext());

        await service.RestoreAsync(BackupJson.Serialize(backup), CancellationToken.None);
        var restored = BackupJson.Deserialize(await service.ExportBackupAsync(CancellationToken.None));

        var restoredHuf = Assert.Single(restored.Accounts, item => item.Name == huf.Name);
        var restoredEur = Assert.Single(restored.Accounts, item => item.Name == eur.Name);
        var restoredParent = Assert.Single(restored.Categories, item => item.Name == parent.Name);
        var restoredChild = Assert.Single(restored.Categories, item => item.Name == child.Name);
        var restoredDebt = Assert.Single(restored.Debts!, item => item.Name == debt.Name);
        Assert.Equal(huf with { Id = restoredHuf.Id }, restoredHuf);
        Assert.Equal(eur with { Id = restoredEur.Id }, restoredEur);
        Assert.Null(restoredParent.ParentCategoryId);
        Assert.Equal(restoredParent.Id, restoredChild.ParentCategoryId);
        Assert.Equal(restoredHuf.Id, restoredDebt.AccountId);
        Assert.Null(Assert.Single(restored.Debts!, item => item.Name == "Unlinked").AccountId);
        var restoredExpense = Assert.Single(restored.Transactions, item => item.Note == "Groceries");
        Assert.Equal(expense with { Id = restoredExpense.Id, AccountId = restoredHuf.Id, CategoryId = restoredChild.Id }, restoredExpense);
        var restoredTransfer = Assert.Single(restored.Transactions, item => item.Type == TransactionType.Transfer);
        Assert.Equal(transfer with { Id = restoredTransfer.Id, AccountId = restoredHuf.Id, TargetAccountId = restoredEur.Id }, restoredTransfer);
        var restoredPayment = Assert.Single(restored.Transactions, item => item.DebtOperationType == DebtOperationType.Payment);
        Assert.Equal(payment with { Id = restoredPayment.Id, AccountId = restoredHuf.Id, DebtId = restoredDebt.Id }, restoredPayment);
        var restoredIncrease = Assert.Single(restored.Transactions, item => item.DebtOperationType == DebtOperationType.Increase);
        Assert.Equal(increase with { Id = restoredIncrease.Id, DebtId = restoredDebt.Id }, restoredIncrease);
        var restoredRecurring = Assert.Single(restored.RecurringTransactions, item => item.DebtId is null);
        Assert.Equal(recurring with { Id = restoredRecurring.Id, AccountId = restoredHuf.Id, CategoryId = restoredChild.Id }, restoredRecurring);
        var restoredRecurringPayment = Assert.Single(restored.RecurringTransactions, item => item.DebtId is not null);
        Assert.Equal(recurringPayment with { Id = restoredRecurringPayment.Id, AccountId = restoredHuf.Id, DebtId = restoredDebt.Id }, restoredRecurringPayment);
        Assert.Empty(BackupValidator.Validate(restored));
    }

    [Fact]
    public async Task Restore_PreservesDebtOperationOrderForIdenticalTimestamps()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(Guid.NewGuid()));
        await CreateSqliteSchemaAsync(db);
        var backup = ValidBackup(); var debt = backup.Debts![0];
        var increase = DebtOperation(debt.Id, null, DebtOperationType.Increase, 100, new DateOnly(2026, 1, 2)) with { Id = Guid.Parse("00000001-0000-0000-0000-000000000000") };
        var payment = DebtOperation(debt.Id, debt.AccountId, DebtOperationType.Payment, 200, increase.TransactionDate) with { Id = Guid.Parse("00000002-0000-0000-0000-000000000000") };
        backup = backup with { Transactions = [payment, increase] };
        var service = new ImportExportService(db, null!, new TestUserContext());

        await service.RestoreAsync(BackupJson.Serialize(backup), CancellationToken.None);
        var restored = BackupJson.Deserialize(await service.ExportBackupAsync(CancellationToken.None));

        Assert.Equal(new[] { DebtOperationType.Increase, DebtOperationType.Payment }, restored.Transactions.OrderBy(item => item.Id).Select(item => item.DebtOperationType!.Value));
        Assert.Empty(BackupValidator.Validate(restored));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Restore_BackupWithoutDebts_PreservesNullableReferences(int version)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PocketLedgerDbContext>().UseSqlite(connection).Options;
        await using var db = new PocketLedgerDbContext(options, new TestCurrentUser(Guid.NewGuid()));
        await CreateSqliteSchemaAsync(db);
        var backup = ValidBackup() with { Version = version, Debts = null };
        var service = new ImportExportService(db, null!, new TestUserContext());

        await service.RestoreAsync(BackupJson.Serialize(backup), CancellationToken.None);
        var restored = BackupJson.Deserialize(await service.ExportBackupAsync(CancellationToken.None));

        Assert.Empty(restored.Debts!);
        Assert.Null(Assert.Single(restored.Categories).ParentCategoryId);
        var transaction = Assert.Single(restored.Transactions);
        Assert.Null(transaction.DebtId);
        Assert.Null(transaction.TargetAccountId);
        Assert.Contains(restored.Accounts, item => item.Id == transaction.AccountId);
        Assert.Equal(Assert.Single(restored.Categories).Id, transaction.CategoryId);
        Assert.Null(Assert.Single(restored.RecurringTransactions).DebtId);
        Assert.Empty(BackupValidator.Validate(restored));
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

    private static TransactionBackup DebtOperation(Guid debtId, Guid? accountId, DebtOperationType operationType, decimal amount, DateOnly date)
    {
        var type = accountId is null ? TransactionType.DebtEntry : TransactionType.Expense;
        return new TransactionBackup(Guid.NewGuid(), type, accountId, null, amount, null, null, date, null, null, DebtId: debtId, DebtOperationType: operationType, SourceCurrency: "HUF");
    }

    private static Task CreateSqliteSchemaAsync(PocketLedgerDbContext db)
    {
        return db.Database.EnsureCreatedAsync();
    }

    private static void AssertRule(IReadOnlyList<string> errors, Guid id, string rule) => Assert.Contains(errors, error => error.Contains(id.ToString()) && error.Contains($"rule '{rule}'"));
    private sealed class TestCurrentUser(Guid userId) : ICurrentUser { public Guid UserId => userId; public bool IsAuthenticated => true; }

    private sealed class TestUserContext(DateOnly today = default) : IUserContextService
    {
        public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => Task.FromResult(today == default ? new DateOnly(2026, 9, 3) : today);
        public Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public string Format(decimal amount, string? currency) => throw new NotSupportedException();
        public string FormatNumber(decimal amount, string? currency) => throw new NotSupportedException();
        public MoneyInputFormat GetMoneyInputFormat(string currency) => throw new NotSupportedException();
    }

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
