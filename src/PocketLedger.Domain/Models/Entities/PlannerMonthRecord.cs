namespace PocketLedger.Models.Entities;

public class PlannerMonthRecord
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public DateOnly Month { get; set; }
    public bool IsClosed { get; set; }
    public string OpeningBalancesJson { get; set; } = "[]";
    public string SnapshotJson { get; set; } = string.Empty;
}
