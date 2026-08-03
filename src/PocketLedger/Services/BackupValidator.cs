namespace PocketLedger.Services;

public static class BackupValidator
{
    public static IReadOnlyList<string> Validate(PocketLedgerBackup backup)
    {
        var errors = new List<string>();
        if (backup.Version != 1) errors.Add("Unsupported backup version.");
        if (backup.Accounts.Select(item => item.Id).Distinct().Count() != backup.Accounts.Count) errors.Add("Duplicate account IDs.");
        if (backup.Categories.Select(item => item.Id).Distinct().Count() != backup.Categories.Count) errors.Add("Duplicate category IDs.");
        if (backup.Transactions.Select(item => item.Id).Distinct().Count() != backup.Transactions.Count) errors.Add("Duplicate transaction IDs.");
        var accountIds = backup.Accounts.Select(item => item.Id).ToHashSet();
        var categoryIds = backup.Categories.Select(item => item.Id).ToHashSet();
        if (backup.Categories.Any(item => item.ParentCategoryId is not null && !categoryIds.Contains(item.ParentCategoryId.Value))) errors.Add("Category references a missing parent.");
        if (backup.Transactions.Any(item => !accountIds.Contains(item.AccountId) || item.TargetAccountId is not null && !accountIds.Contains(item.TargetAccountId.Value))) errors.Add("Transaction references a missing account.");
        if (backup.Transactions.Any(item => item.CategoryId is not null && !categoryIds.Contains(item.CategoryId.Value))) errors.Add("Transaction references a missing category.");
        if (backup.RecurringTransactions.Any(item => !accountIds.Contains(item.AccountId) || item.CategoryId is not null && !categoryIds.Contains(item.CategoryId.Value))) errors.Add("Recurring transaction contains an invalid reference.");
        return errors;
    }
}
