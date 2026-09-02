using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public sealed record TransactionCreateInput(TransactionType Type, Guid? AccountId, Guid? TargetAccountId, decimal Amount, decimal? TargetAmount, decimal? ExchangeRate, AdjustmentDirection? AdjustmentDirection, DateOnly TransactionDate, TimeOnly TransactionTime, Guid? CategoryId, string? Note);

public sealed record TransactionUpdateInput(TransactionType Type, Guid? AccountId, Guid? TargetAccountId, decimal Amount, decimal? TargetAmount, decimal? ExchangeRate, AdjustmentDirection? AdjustmentDirection, DateOnly TransactionDate, TimeOnly TransactionTime, Guid? CategoryId, string? Note);
