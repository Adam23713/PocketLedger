using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class RecurringTransaction
{
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public Guid Id { get; set; }
    public TransactionType Type { get; set; }
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public decimal Amount { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }
    public string? Note { get; set; }
    public DateOnly FirstOccurrence { get; set; }
    public DateOnly? LastOccurrence { get; set; }
    public DateOnly AutomationStartsOn { get; set; }
    public RecurringFrequency Frequency { get; set; }
    public bool Enabled { get; set; }
    public Guid? DebtId { get; set; }
    public Debt? Debt { get; set; }
    public DebtOperationType? DebtOperationType { get; set; }
    public ICollection<RecurringTransactionOccurrence> Occurrences { get; set; } = [];
}
