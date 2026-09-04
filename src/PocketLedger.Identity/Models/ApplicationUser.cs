using Microsoft.AspNetCore.Identity;

namespace PocketLedger.Models.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessfulLoginAtUtc { get; set; }
    public string? LastSuccessfulLoginIpAddress { get; set; }
    public bool AuthenticatorSetupComplete { get; set; }
}
