using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class Debt
{
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public DebtDirection Direction { get; set; }
    public DebtType Type { get; set; }
    public string CounterpartyName { get; set; } = string.Empty;
    public decimal OriginalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Note { get; set; }
    public DebtStatus Status { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? AccountId { get; set; }
    public Account? Account { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = [];
    public ICollection<RecurringTransaction> RecurringTransactions { get; set; } = [];
}
