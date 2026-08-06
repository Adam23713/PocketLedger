using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class Transaction
{
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid? TargetAccountId { get; set; }
    public Account? TargetAccount { get; set; }
    public decimal Amount { get; set; }
    public decimal? TargetAmount { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }
    public DateOnly TransactionDate { get; set; }
    public TimeOnly TransactionTime { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? Note { get; set; }
}
