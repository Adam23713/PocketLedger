using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public record AccountBackup(Guid Id, string Name, AccountType Type, string Currency, decimal InitialBalance, string? Icon, int DisplayOrder, bool IncludeInMainBalance, bool IncludeInNetWorth, bool IncludeInStatistics, string? Color = null);
public record CategoryBackup(Guid Id, string Name, CategoryType Type, string? Icon, Guid? ParentCategoryId, int DisplayOrder);
public record TransactionBackup(Guid Id, TransactionType Type, Guid? AccountId, Guid? TargetAccountId, decimal Amount, decimal? TargetAmount, AdjustmentDirection? AdjustmentDirection, DateOnly TransactionDate, Guid? CategoryId, string? Note, TimeOnly TransactionTime = default, Guid? DebtId = null, DebtOperationType? DebtOperationType = null, decimal? ExchangeRate = null, string SourceCurrency = "HUF", string? TargetCurrency = null, DateTimeOffset OccurredAtUtc = default);
public record RecurringTransactionBackup(Guid Id, TransactionType Type, Guid AccountId, Guid? CategoryId, decimal Amount, AdjustmentDirection? AdjustmentDirection, string? Note, DateOnly FirstOccurrence, DateOnly? LastOccurrence, RecurringFrequency Frequency, bool Enabled, Guid? DebtId = null, DebtOperationType? DebtOperationType = null);
public record DebtBackup(Guid Id, string Name, DebtDirection Direction, DebtType Type, string CounterpartyName, decimal OriginalAmount, string Currency, DateOnly StartDate, DateOnly? DueDate, string? Note, DebtStatus Status, DateTimeOffset? ClosedAt, Guid? AccountId, string? Icon = null);
public record PocketLedgerBackup(int Version, DateTimeOffset ExportedAt, IReadOnlyList<AccountBackup> Accounts, IReadOnlyList<CategoryBackup> Categories, IReadOnlyList<TransactionBackup> Transactions, IReadOnlyList<RecurringTransactionBackup> RecurringTransactions, IReadOnlyList<DebtBackup>? Debts = null);
public record RestorePreview(bool IsValid, int AccountCount, int CategoryCount, int TransactionCount, int RecurringTransactionCount, IReadOnlyList<string> Errors);
public record CsvImportRow(int RowNumber, bool IsValid, bool IsDuplicate, string? Error, DateOnly? Date, string Account, TransactionType? Type, string? Category, decimal? Amount, string Currency, string? Note);
public record CsvImportPreview(IReadOnlyList<CsvImportRow> Rows)
{
    public int ValidCount => Rows.Count(row => row.IsValid && !row.IsDuplicate);
    public int InvalidCount => Rows.Count(row => !row.IsValid);
    public int DuplicateCount => Rows.Count(row => row.IsDuplicate);
}
public record CsvImportResult(int ImportedCount, int InvalidCount, int DuplicateCount);
