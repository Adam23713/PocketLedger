using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class BackupValidator
{
    public static IReadOnlyList<string> Validate(PocketLedgerBackup backup)
    {
        var errors = new List<string>();
        if (backup.Version is not (1 or 2))
        {
            errors.Add("Backup: rule 'version': Unsupported backup version.");
        }

        AddDuplicates(errors, "Account", backup.Accounts.Select(item => item.Id));
        AddDuplicates(errors, "Category", backup.Categories.Select(item => item.Id));
        AddDuplicates(errors, "Transaction", backup.Transactions.Select(item => item.Id));
        AddDuplicates(errors, "RecurringTransaction", backup.RecurringTransactions.Select(item => item.Id));
        var debts = backup.Debts ?? [];
        AddDuplicates(errors, "Debt", debts.Select(item => item.Id));
        var accounts = backup.Accounts.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var categories = backup.Categories.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        var debtLookup = debts.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.First());
        foreach (var account in backup.Accounts)
        {
            TryRule(errors, "Account", account.Id, "account", () => AccountRules.Validate(account.Name, account.Type, account.DisplayOrder));
            ValidateCurrency(errors, "Account", account.Id, "currency", account.Currency);
        }
        foreach (var category in backup.Categories) ValidateCategory(category, categories, errors);
        foreach (var debt in debts) ValidateDebt(debt, accounts, errors);
        foreach (var transaction in backup.Transactions) ValidateTransaction(transaction, accounts, categories, debtLookup, errors);
        ValidateDebtBalances(debts, backup.Transactions, errors);
        foreach (var recurring in backup.RecurringTransactions) ValidateRecurring(recurring, accounts, categories, debtLookup, errors);
        return errors;
    }

    private static void ValidateCategory(CategoryBackup item, IReadOnlyDictionary<Guid, CategoryBackup> categories, List<string> errors)
    {
        TryRule(errors, "Category", item.Id, "category", () => CategoryRules.Validate(item.Name, item.Type, item.DisplayOrder, null));
        TryRule(errors, "Category", item.Id, "icon", () => CategoryRules.ValidateIcon(item.Icon, item.ParentCategoryId is not null));
        if (item.ParentCategoryId is not { } parentId)
        {
            return;
        }

        if (!categories.TryGetValue(parentId, out var parent))
        {
            Add(errors, "Category", item.Id, "parent-reference", $"Parent category {parentId} does not exist in the backup.");
            return;
        }
        var childEntity = new Category { Id = item.Id, Type = item.Type, Icon = item.Icon, ParentCategoryId = item.ParentCategoryId };
        var parentEntity = new Category { Id = parent.Id, Type = parent.Type, ParentCategoryId = parent.ParentCategoryId };
        TryRule(errors, "Category", item.Id, "parent-reference", () => CategoryRules.Validate(item.Name, item.Type, item.DisplayOrder, parentEntity));
        TryRule(errors, "Category", item.Id, "category-type", () => CategoryRules.ValidateParent(childEntity, parentEntity));
    }

    private static void ValidateDebt(DebtBackup item, IReadOnlyDictionary<Guid, AccountBackup> accounts, List<string> errors)
    {
        ValidateCurrency(errors, "Debt", item.Id, "currency", item.Currency);
        var account = Lookup(accounts, item.AccountId, errors, "Debt", item.Id, "account-reference");
        var entity = new Debt
        {
            Id = item.Id,
            Name = item.Name,
            Icon = item.Icon ?? PocketLedger.Models.CategoryIcons.DefaultFor(item.Direction == DebtDirection.Receivable ? CategoryType.Income : CategoryType.Expense).Id,
            Direction = item.Direction,
            Type = item.Type,
            CounterpartyName = item.CounterpartyName,
            OriginalAmount = item.OriginalAmount,
            Currency = item.Currency,
            StartDate = item.StartDate,
            DueDate = item.DueDate,
            Status = item.Status,
            ClosedAt = item.ClosedAt,
            AccountId = item.AccountId
        };
        TryRule(errors, "Debt", item.Id, "debt", () => DebtRules.Validate(entity, account is null ? null : ToEntity(account)));
        if (account is not null && !SameCurrency(item.Currency, account.Currency)) Add(errors, "Debt", item.Id, "currency-consistency", "Debt and account currencies must match.");
    }

    private static void ValidateTransaction(TransactionBackup item, IReadOnlyDictionary<Guid, AccountBackup> accounts, IReadOnlyDictionary<Guid, CategoryBackup> categories, IReadOnlyDictionary<Guid, DebtBackup> debts, List<string> errors)
    {
        var account = Lookup(accounts, item.AccountId, errors, "Transaction", item.Id, "source-account-reference");
        var target = Lookup(accounts, item.TargetAccountId, errors, "Transaction", item.Id, "target-account-reference");
        var category = Lookup(categories, item.CategoryId, errors, "Transaction", item.Id, "category-reference");
        var debt = Lookup(debts, item.DebtId, errors, "Transaction", item.Id, "debt-reference");
        ValidateCurrency(errors, "Transaction", item.Id, "source-currency", item.SourceCurrency);
        if (item.TargetCurrency is not null)
        {
            ValidateCurrency(errors, "Transaction", item.Id, "target-currency", item.TargetCurrency);
        }
        if (account is not null && !SameCurrency(item.SourceCurrency, account.Currency)) Add(errors, "Transaction", item.Id, "currency-consistency", "Source currency must match the source account currency.");
        if (target is not null && !SameCurrency(item.TargetCurrency, target.Currency)) Add(errors, "Transaction", item.Id, "currency-consistency", "Target currency must match the target account currency.");
        TryRule(errors, "Transaction", item.Id, "financial-semantics", () => TransactionSemantics.Resolve(item.Type, item.Amount, item.TargetAmount, item.AdjustmentDirection, item.DebtOperationType));
        if (item.DebtId is not null || item.DebtOperationType is not null)
        {
            ValidateDebtTransaction(item, account, debt, errors);
            return;
        }

        var entity = ToEntity(item);
        var sourceEntity = account is null ? null : ToEntity(account);
        var categoryEntity = category is null ? null : ToEntity(category);
        if (item.Type == TransactionType.Transfer) TryRule(errors, "Transaction", item.Id, "transfer", () => TransactionRules.ValidateTransfer(entity, sourceEntity, target is null ? null : ToEntity(target)));
        else TryRule(errors, "Transaction", item.Id, "transaction", () => TransactionRules.Validate(entity, sourceEntity, categoryEntity));
        if (item.Type != TransactionType.Transfer && item.TargetCurrency is not null) Add(errors, "Transaction", item.Id, "target-currency", "Target currency is only valid for transfers.");
    }

    private static void ValidateDebtBalances(IReadOnlyList<DebtBackup> debts, IReadOnlyList<TransactionBackup> transactions, List<string> errors)
    {
        foreach (var debt in debts)
        {
            var remaining = debt.OriginalAmount;
            var operations = transactions
                .Where(item => item.DebtId == debt.Id && item.DebtOperationType is not null && Enum.IsDefined(item.DebtOperationType.Value) && item.Amount > 0)
                .OrderBy(item => item.TransactionDate)
                .ThenBy(item => item.TransactionTime)
                .ThenBy(item => item.OccurredAtUtc)
                .ThenBy(item => item.Id);

            foreach (var operation in operations)
            {
                var nextRemaining = remaining + DebtRules.GetDebtDelta(operation.DebtOperationType!.Value, operation.Amount);
                if (nextRemaining < 0)
                {
                    Add(errors, "Transaction", operation.Id, "debt-balance", "The operation amount cannot exceed the remaining debt at this point in the operation sequence.");
                    continue;
                }

                remaining = nextRemaining;
            }
        }
    }

    private static void ValidateDebtTransaction(TransactionBackup item, AccountBackup? account, DebtBackup? debt, List<string> errors)
    {
        if (item.DebtId is null || item.DebtOperationType is null)
        {
            Add(errors, "Transaction", item.Id, "debt-operation", "Debt ID and debt operation type must either both be set or both be empty.");
            return;
        }

        if (!Enum.IsDefined(item.DebtOperationType.Value))
        {
            Add(errors, "Transaction", item.Id, "debt-operation", "Debt operation type is invalid.");
            return;
        }

        if (debt is null) return;
        if (!SameCurrency(item.SourceCurrency, debt.Currency)) Add(errors, "Transaction", item.Id, "currency-consistency", "Transaction and debt currencies must match.");
        if (item.CategoryId is not null || item.TargetAccountId is not null || item.TargetAmount is not null || item.TargetCurrency is not null || item.AdjustmentDirection is not null) Add(errors, "Transaction", item.Id, "debt-operation", "Debt operations contain unsupported transaction fields.");
        if (DebtRules.RequiresAccount(item.DebtOperationType.Value) && item.AccountId is null) Add(errors, "Transaction", item.Id, "debt-operation", "This debt operation requires an account.");
        if (!DebtRules.AllowsAccount(item.DebtOperationType.Value) && item.AccountId is not null) Add(errors, "Transaction", item.Id, "debt-operation", "This debt operation cannot use an account.");
        if (item.Amount <= 0 || item.TransactionDate == default) Add(errors, "Transaction", item.Id, "debt-operation", "A positive amount and date are required.");
        if (item.DebtOperationType is DebtOperationType.ManualCorrectionIncrease or DebtOperationType.ManualCorrectionDecrease && string.IsNullOrWhiteSpace(item.Note)) Add(errors, "Transaction", item.Id, "debt-operation", "A note is required for a manual correction.");
        if (account is not null && !SameCurrency(account.Currency, debt.Currency)) Add(errors, "Transaction", item.Id, "currency-consistency", "Debt and account currencies must match.");
        var expectedType = TransactionSemantics.GetDebtTransactionType(item.DebtOperationType.Value, account is not null);
        if (item.Type != expectedType) Add(errors, "Transaction", item.Id, "debt-operation", $"Debt operation requires transaction type {expectedType}.");
        if (debt.Direction == DebtDirection.Payable && item.DebtOperationType is DebtOperationType.LoanDisbursement or DebtOperationType.ReceivedRepayment || debt.Direction == DebtDirection.Receivable && item.DebtOperationType is DebtOperationType.Payment or DebtOperationType.EarlyRepayment) Add(errors, "Transaction", item.Id, "debt-operation", "Debt operation does not match the debt direction.");
    }

    private static void ValidateRecurring(RecurringTransactionBackup item, IReadOnlyDictionary<Guid, AccountBackup> accounts, IReadOnlyDictionary<Guid, CategoryBackup> categories, IReadOnlyDictionary<Guid, DebtBackup> debts, List<string> errors)
    {
        var account = Lookup(accounts, item.AccountId, errors, "RecurringTransaction", item.Id, "account-reference");
        var category = Lookup(categories, item.CategoryId, errors, "RecurringTransaction", item.Id, "category-reference");
        var debt = Lookup(debts, item.DebtId, errors, "RecurringTransaction", item.Id, "debt-reference");
        var entity = ToEntity(item);
        if (item.DebtId is null && item.DebtOperationType is null)
        {
            TryRule(errors, "RecurringTransaction", item.Id, "recurring-transaction", () => RecurringTransactionRules.Validate(entity, account is null ? null : ToEntity(account), category is null ? null : ToEntity(category)));
        }
        else
        {
            if (item.DebtId is null || item.DebtOperationType is null)
            {
                Add(errors, "RecurringTransaction", item.Id, "debt-operation", "Debt ID and debt operation type must either both be set or both be empty.");
                return;
            }
            if (debt is not null && account is not null && !SameCurrency(debt.Currency, account.Currency)) Add(errors, "RecurringTransaction", item.Id, "currency-consistency", "Debt and account currencies must match.");
            var expectedOperation = debt?.Direction == DebtDirection.Payable ? DebtOperationType.Payment : DebtOperationType.ReceivedRepayment;
            var expectedType = debt is null ? item.Type : TransactionSemantics.GetDebtTransactionType(expectedOperation, true);
            if (debt is not null && (item.DebtOperationType != expectedOperation || item.Type != expectedType || item.CategoryId is not null || item.AdjustmentDirection is not null)) Add(errors, "RecurringTransaction", item.Id, "debt-operation", "Recurring debt operation does not match the debt direction or contains unsupported fields.");
            TryRule(errors, "RecurringTransaction", item.Id, "recurring-schedule", () => RecurringTransactionRules.ValidateSchedule(entity));
            if (item.Amount <= 0) Add(errors, "RecurringTransaction", item.Id, "recurring-transaction", "Amount must be greater than zero.");
        }
    }

    private static Account ToEntity(AccountBackup item) => new() { Id = item.Id, Currency = item.Currency };
    private static Category ToEntity(CategoryBackup item) => new() { Id = item.Id, Type = item.Type };
    private static Transaction ToEntity(TransactionBackup item) => new() { Id = item.Id, Type = item.Type, AccountId = item.AccountId, TargetAccountId = item.TargetAccountId, Amount = item.Amount, TargetAmount = item.TargetAmount, ExchangeRate = item.ExchangeRate, SourceCurrency = item.SourceCurrency, TargetCurrency = item.TargetCurrency, AdjustmentDirection = item.AdjustmentDirection, TransactionDate = item.TransactionDate, CategoryId = item.CategoryId, DebtId = item.DebtId, DebtOperationType = item.DebtOperationType };
    private static RecurringTransaction ToEntity(RecurringTransactionBackup item) => new() { Id = item.Id, Type = item.Type, AccountId = item.AccountId, CategoryId = item.CategoryId, Amount = item.Amount, AdjustmentDirection = item.AdjustmentDirection, FirstOccurrence = item.FirstOccurrence, LastOccurrence = item.LastOccurrence, Frequency = item.Frequency, Enabled = item.Enabled, DebtId = item.DebtId, DebtOperationType = item.DebtOperationType };

    private static TValue? Lookup<TValue>(IReadOnlyDictionary<Guid, TValue> values, Guid? id, List<string> errors, string recordType, Guid recordId, string rule) where TValue : class
    {
        if (id is null) return null;
        if (values.TryGetValue(id.Value, out var value)) return value;
        Add(errors, recordType, recordId, rule, $"Referenced record {id.Value} does not exist in the backup.");
        return null;
    }

    private static void AddDuplicates(List<string> errors, string recordType, IEnumerable<Guid> ids)
    {
        foreach (var id in ids.GroupBy(id => id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            Add(errors, recordType, id, "unique-id", "Record ID occurs more than once.");
        }
    }

    private static void TryRule(List<string> errors, string recordType, Guid recordId, string rule, Action action)
    {
        try
        {
            action();
        }
        catch (BusinessRuleException exception)
        {
            Add(errors, recordType, recordId, rule, exception.Message);
        }
    }

    private static void ValidateCurrency(List<string> errors, string recordType, Guid recordId, string rule, string? currency)
    {
        try
        {
            var normalized = AccountRules.NormalizeAndValidateCurrency(currency);
            if (!string.Equals(currency, normalized, StringComparison.Ordinal))
            {
                Add(errors, recordType, recordId, rule, $"Currency must use the canonical code '{normalized}'.");
            }
        }
        catch (BusinessRuleException exception)
        {
            Add(errors, recordType, recordId, rule, exception.Message);
        }
    }
    private static bool SameCurrency(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static void Add(List<string> errors, string recordType, Guid recordId, string rule, string message) => errors.Add($"{recordType} {recordId}: rule '{rule}': {message}");
}
