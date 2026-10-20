namespace PocketLedger.Models.Entities;

// Single-row table: once an administrator saves the registration settings, this row overrides the configured defaults.
public sealed class RegistrationSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool IsOpen { get; set; }
    public int? MaximumUserCount { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
