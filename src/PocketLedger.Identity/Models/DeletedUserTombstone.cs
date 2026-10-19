namespace PocketLedger.Models.Entities;

public sealed class DeletedUserTombstone
{
    public Guid UserId { get; set; }
    public DateTimeOffset DeletedAtUtc { get; set; }
}
