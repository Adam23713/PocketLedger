using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Configuration;

public sealed class CrowdSecOptions
{
    public const string SectionName = "CrowdSec";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    // A machine account, separate from the read-only bouncer ApiKey. Leaving it empty keeps the dashboard read-only.
    public string? MachineId { get; set; }
    public string? MachinePassword { get; set; }
    [Range(1, 30)] public int TimeoutSeconds { get; set; } = 3;
}
