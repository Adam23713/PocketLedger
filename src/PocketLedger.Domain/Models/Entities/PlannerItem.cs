using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class PlannerItem
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public bool IsPaused { get; set; }
    public DateOnly Month { get; set; }
    public int? CopyDay { get; set; }
    public DateOnly? PlannedDate { get; set; }
    public TransactionType Type { get; set; }
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid? TargetAccountId { get; set; }
    public Account? TargetAccount { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal AccountAmount { get; set; }
    public decimal? TargetAmount { get; set; }
    public string? Note { get; set; }
}
