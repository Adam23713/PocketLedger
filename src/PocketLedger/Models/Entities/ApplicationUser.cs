using Microsoft.AspNetCore.Identity;

namespace PocketLedger.Models.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public int AvatarId { get; set; } = 1;
    public string DefaultCurrency { get; set; } = "HUF";
    public string TimeZoneId { get; set; } = "Europe/Budapest";
    public ICollection<UserCurrencyFormat> CurrencyFormats { get; set; } = [];
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSuccessfulLoginAtUtc { get; set; }
    public string? LastSuccessfulLoginIpAddress { get; set; }
    public bool AuthenticatorSetupComplete { get; set; }
}
