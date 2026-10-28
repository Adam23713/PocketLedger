namespace PocketLedger.Models.Entities;

public sealed class UserPreference
{
    public Guid UserId { get; set; }
    public string? DisplayName { get; set; }
    public int AvatarId { get; set; } = 1;
    public string DefaultCurrency { get; set; } = "EUR";
    public string TimeZoneId { get; set; } = "UTC";
    public string? LastSeenChangelogVersion { get; set; }
    public ICollection<UserCurrencyFormat> CurrencyFormats { get; set; } = [];
}
