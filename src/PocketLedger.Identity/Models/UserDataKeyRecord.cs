namespace PocketLedger.Models.Entities;

// The user's DEK, stored only wrapped: once by the password (or, for external-login users, the data password)
// and once by the recovery key. Both wraps also receive the host's Data Protection layer at rest.
public sealed class UserDataKeyRecord
{
    public Guid UserId { get; set; }
    public int KeyVersion { get; set; }
    public string PasswordWrap { get; set; } = string.Empty;
    public string RecoveryWrap { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
