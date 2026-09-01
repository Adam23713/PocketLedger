using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Contracts;

public sealed record ApiError(string Code, string Message);
public sealed record AccountUpdateRequest(AccountDto Account, bool CreateInitialBalanceAdjustment);
public sealed record DebtWriteRequest(DebtDto Debt, RecurringPaymentInput? RecurringPayment);
public sealed record DebtOperationWriteRequest(DebtOperationInput Operation);
public sealed record TextPayload(string Content);
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
public sealed record DebtSummaryResponse(DebtDto Debt, decimal RemainingAmount, RecurringTransactionDto? AutomaticPayment, DateOnly? NextPayment);
public sealed record DebtDetailsResponse(DebtDto Debt, decimal RemainingAmount, IReadOnlyList<TransactionDto> Transactions, RecurringTransactionDto? AutomaticPayment, DateOnly? NextPayment);
public sealed record UserPreferenceResponse(string? DisplayName, int AvatarId, string DefaultCurrency, string TimeZoneId, IReadOnlyList<UserCurrencyFormatDto> CurrencyFormats);
public sealed record UserPreferenceUpdateRequest(string? DisplayName, int AvatarId, string DefaultCurrency, string TimeZoneId, IReadOnlyList<UserCurrencyFormatDto> CurrencyFormats);

public sealed record AccountDto(Guid Id, string Name, AccountType Type, string Currency, decimal InitialBalance, string? Icon, string Color, int DisplayOrder, bool IncludeInMainBalance, bool IncludeInNetWorth, bool IncludeInStatistics);
public sealed record CategoryDto(Guid Id, string Name, CategoryType Type, string? Icon, Guid? ParentCategoryId, CategoryDto? ParentCategory, IReadOnlyList<CategoryDto> Subcategories, int DisplayOrder);
public sealed record DebtDto(Guid Id, string Name, string Icon, DebtDirection Direction, DebtType Type, string CounterpartyName, decimal OriginalAmount, string Currency, DateOnly StartDate, DateOnly? DueDate, string? Note, DebtStatus Status, DateTimeOffset? ClosedAt, Guid? AccountId, AccountDto? Account);
public sealed record RecurringTransactionDto(Guid Id, TransactionType Type, Guid AccountId, AccountDto? Account, Guid? CategoryId, CategoryDto? Category, decimal Amount, AdjustmentDirection? AdjustmentDirection, string? Note, DateOnly FirstOccurrence, DateOnly? LastOccurrence, DateOnly AutomationStartsOn, RecurringFrequency Frequency, bool Enabled, Guid? DebtId, DebtDto? Debt, DebtOperationType? DebtOperationType);
public sealed record TransactionDto(Guid Id, TransactionType Type, Guid? AccountId, AccountDto? Account, Guid? TargetAccountId, AccountDto? TargetAccount, decimal Amount, decimal? TargetAmount, decimal? ExchangeRate, string SourceCurrency, string? TargetCurrency, AdjustmentDirection? AdjustmentDirection, DateOnly TransactionDate, TimeOnly TransactionTime, DateTimeOffset OccurredAtUtc, Guid? CategoryId, CategoryDto? Category, string? Note, Guid? DebtId, DebtDto? Debt, DebtOperationType? DebtOperationType);
public sealed record UserCurrencyFormatDto(string CurrencyCode, int DecimalPlaces, string DecimalSeparator, string ThousandsSeparator, CurrencyDisplay CurrencyDisplay, CurrencyPosition CurrencyPosition, bool UseSpace);
