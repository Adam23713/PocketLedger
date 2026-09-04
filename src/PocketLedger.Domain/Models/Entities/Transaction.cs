using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class Transaction
{
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public Guid? TargetAccountId { get; set; }
    public Account? TargetAccount { get; set; }
    public decimal Amount { get; set; }
    public decimal? TargetAmount { get; set; }
    public decimal? ExchangeRate { get; set; }
    public string SourceCurrency { get; set; } = string.Empty;
    public string? TargetCurrency { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }
    public DateOnly TransactionDate { get; set; }
    public TimeOnly TransactionTime { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Note { get; set; }
    public Guid? DebtId { get; set; }
    public Debt? Debt { get; set; }
    public DebtOperationType? DebtOperationType { get; set; }
}
