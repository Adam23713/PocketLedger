namespace PocketLedger.Models.Entities;

/// <summary>
/// A user whose access tokens the API must reject. Identity is the source of truth; it pushes every
/// block here immediately and re-synchronizes the full list periodically.
/// </summary>
public sealed class BlockedUser
{
    public Guid UserId { get; set; }
    public DateTimeOffset BlockedAtUtc { get; set; }
}
