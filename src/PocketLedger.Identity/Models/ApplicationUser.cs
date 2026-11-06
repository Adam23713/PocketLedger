using Microsoft.AspNetCore.Identity;

namespace PocketLedger.Models.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessfulLoginAtUtc { get; set; }
    public string? LastSuccessfulLoginIpAddress { get; set; }
    public bool AuthenticatorSetupComplete { get; set; }
    public bool IsAccessBlocked { get; set; }
    public bool IsBootstrapAdministrator { get; private set; }
    // How the account was created: "Password" for local registration, otherwise the external login provider (Google, Facebook).
    public string? RegistrationProvider { get; set; }

    public static ApplicationUser CreateBootstrapAdministrator(string username)
        => new() { Id = Guid.NewGuid(), UserName = username, CreatedAtUtc = DateTimeOffset.UtcNow, IsBootstrapAdministrator = true, RegistrationProvider = "Bootstrap" };
}
