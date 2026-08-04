namespace PocketLedger.Models.Entities;

public class RecurringTransactionOccurrence
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public ApplicationUser Owner { get; set; } = null!;
    public Guid RecurringTransactionId { get; set; }
    public RecurringTransaction RecurringTransaction { get; set; } = null!;
    public DateOnly OccurrenceDate { get; set; }
}
