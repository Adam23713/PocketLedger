using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public record AccountBackup(Guid Id, string Name, AccountType Type, string Currency, decimal InitialBalance, string? Icon, int DisplayOrder, bool IncludeInMainBalance, bool IncludeInNetWorth, bool IncludeInStatistics, string? Color = null);
public record CategoryBackup(Guid Id, string Name, CategoryType Type, string? Icon, Guid? ParentCategoryId, int DisplayOrder);
public record TransactionBackup(Guid Id, TransactionType Type, Guid AccountId, Guid? TargetAccountId, decimal Amount, decimal? TargetAmount, AdjustmentDirection? AdjustmentDirection, DateOnly TransactionDate, Guid? CategoryId, string? Note);
public record RecurringTransactionBackup(Guid Id, TransactionType Type, Guid AccountId, Guid? CategoryId, decimal Amount, AdjustmentDirection? AdjustmentDirection, string? Note, DateOnly FirstOccurrence, DateOnly? LastOccurrence, RecurringFrequency Frequency, bool Enabled);
public record PocketLedgerBackup(int Version, DateTimeOffset ExportedAt, IReadOnlyList<AccountBackup> Accounts, IReadOnlyList<CategoryBackup> Categories, IReadOnlyList<TransactionBackup> Transactions, IReadOnlyList<RecurringTransactionBackup> RecurringTransactions);
public record RestorePreview(bool IsValid, int AccountCount, int CategoryCount, int TransactionCount, int RecurringTransactionCount, IReadOnlyList<string> Errors);
public record CsvImportRow(int RowNumber, bool IsValid, bool IsDuplicate, string? Error, DateOnly? Date, string Account, TransactionType? Type, string? Category, decimal? Amount, string Currency, string? Note);
public record CsvImportPreview(IReadOnlyList<CsvImportRow> Rows)
{
    public int ValidCount => Rows.Count(row => row.IsValid && !row.IsDuplicate);
    public int InvalidCount => Rows.Count(row => !row.IsValid);
    public int DuplicateCount => Rows.Count(row => row.IsDuplicate);
}
public record CsvImportResult(int ImportedCount, int InvalidCount, int DuplicateCount);
