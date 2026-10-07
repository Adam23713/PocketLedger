using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Configuration;

public sealed class CrowdSecOptions
{
    public const string SectionName = "CrowdSec";
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    [Range(1, 30)] public int TimeoutSeconds { get; set; } = 3;
}
