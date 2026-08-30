using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class Account
{
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public string? Icon { get; set; }
    public string Color { get; set; } = "#ffffff";
    public int DisplayOrder { get; set; }
    public bool IncludeInMainBalance { get; set; }
    public bool IncludeInNetWorth { get; set; }
    public bool IncludeInStatistics { get; set; }
}
