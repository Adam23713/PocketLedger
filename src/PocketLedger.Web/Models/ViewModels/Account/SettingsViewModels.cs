using System.ComponentModel.DataAnnotations;
using PocketLedger.Models;
using PocketLedger.Models.Enums;

namespace PocketLedger.Models.ViewModels.Account;

public sealed class SettingsViewModel : IValidatableObject
{
    [StringLength(100)] public string? DisplayName { get; set; }
    [Range(1, 10)] public int AvatarId { get; set; } = 1;
    [Required] public string DefaultCurrency { get; set; } = "HUF";
    [Required] public string TimeZoneId { get; set; } = "Europe/Budapest";
    public List<CurrencyFormatViewModel> CurrencyFormats { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Currencies.Exists(DefaultCurrency)) yield return new("Unsupported default currency.", [nameof(DefaultCurrency)]);
        string? timeZoneError = null;
        try { UserTimeZones.Get(TimeZoneId); } catch (ArgumentException exception) { timeZoneError = exception.Message; }
        if (timeZoneError is not null) yield return new(timeZoneError, [nameof(TimeZoneId)]);
        if (CurrencyFormats.Select(item => item.CurrencyCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != CurrencyFormats.Count) yield return new("Each currency can only be configured once.", [nameof(CurrencyFormats)]);
        foreach (var item in CurrencyFormats)
        {
            if (!Currencies.Exists(item.CurrencyCode)) yield return new("Unsupported currency.", [nameof(CurrencyFormats)]);
            if (item.DecimalSeparator == item.ThousandsSeparator) yield return new("Decimal and thousands separators must be different.", [nameof(CurrencyFormats)]);
        }
    }
}

public sealed class CurrencyFormatViewModel
{
    public string CurrencyCode { get; set; } = string.Empty;
    [Range(0, 4)] public int DecimalPlaces { get; set; }
    [StringLength(1, MinimumLength = 1), DisplayFormat(ConvertEmptyStringToNull = false)] public string DecimalSeparator { get; set; } = ",";
    [StringLength(1, MinimumLength = 1), DisplayFormat(ConvertEmptyStringToNull = false)] public string ThousandsSeparator { get; set; } = " ";
    public CurrencyDisplay CurrencyDisplay { get; set; }
    public CurrencyPosition CurrencyPosition { get; set; }
    public bool UseSpace { get; set; } = true;
}
