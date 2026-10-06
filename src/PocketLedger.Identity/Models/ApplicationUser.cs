using Microsoft.AspNetCore.Identity;

namespace PocketLedger.Models.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessfulLoginAtUtc { get; set; }
    public string? LastSuccessfulLoginIpAddress { get; set; }
    public bool AuthenticatorSetupComplete { get; set; }
    public bool IsBootstrapAdministrator { get; private set; }

    public static ApplicationUser CreateBootstrapAdministrator(string username)
        => new() { Id = Guid.NewGuid(), UserName = username, CreatedAtUtc = DateTimeOffset.UtcNow, IsBootstrapAdministrator = true };
}
