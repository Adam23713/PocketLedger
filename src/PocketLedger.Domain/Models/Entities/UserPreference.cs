namespace PocketLedger.Models.Entities;

public sealed class UserPreference
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public int AvatarId { get; set; } = 1;
    public string DefaultCurrency { get; set; } = "HUF";
    public string TimeZoneId { get; set; } = "Europe/Budapest";
    public ICollection<UserCurrencyFormat> CurrencyFormats { get; set; } = [];
}
