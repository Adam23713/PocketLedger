using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class UserCurrencyFormat
{
    public Guid UserId { get; set; }
    public UserPreference User { get; set; } = null!;
    public string CurrencyCode { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; }
    public string DecimalSeparator { get; set; } = ".";
    public string ThousandsSeparator { get; set; } = ",";
    public CurrencyDisplay CurrencyDisplay { get; set; }
    public CurrencyPosition CurrencyPosition { get; set; }
    public bool UseSpace { get; set; }
}
