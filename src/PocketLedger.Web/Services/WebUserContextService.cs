using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using PocketLedger.Web.Api;

namespace PocketLedger.Web.Services;

public sealed class WebUserContextService(IPreferencesApiClient preferences, IUserDateProvider userDates) : IUserContextService
{
    private UserPreference? cachedUser;

    public async Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default)
    {
        if (cachedUser is not null) return cachedUser;
        var response = await preferences.GetAsync(cancellationToken);
        cachedUser = new UserPreference { DisplayName = response.DisplayName, AvatarId = response.AvatarId, DefaultCurrency = response.DefaultCurrency, TimeZoneId = response.TimeZoneId, CurrencyFormats = response.CurrencyFormats.Select(item => new UserCurrencyFormat { CurrencyCode = item.CurrencyCode, DecimalPlaces = item.DecimalPlaces, DecimalSeparator = item.DecimalSeparator, ThousandsSeparator = item.ThousandsSeparator, CurrencyDisplay = item.CurrencyDisplay, CurrencyPosition = item.CurrencyPosition, UseSpace = item.UseSpace }).ToList() };
        return cachedUser;
    }

    public async Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default)
    {
        return userDates.ToUtc(date, time, (await GetUserAsync(cancellationToken)).TimeZoneId);
    }

    public async Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => userDates.Today((await GetUserAsync(cancellationToken)).TimeZoneId);
    public async Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default) { await GetUserAsync(cancellationToken); return Format(amount, currency); }
    public string Format(decimal amount, string? currency) => FormatCore(amount, currency, includeMarker: true);
    public string FormatNumber(decimal amount, string? currency) => FormatCore(amount, currency, includeMarker: false);

    public MoneyInputFormat GetMoneyInputFormat(string currency)
    {
        var definition = Currencies.Get(currency);
        var format = Current().CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        return new MoneyInputFormat(format.DecimalPlaces, format.DecimalSeparator, format.ThousandsSeparator, marker, format.CurrencyPosition, format.UseSpace);
    }

    private string FormatCore(decimal amount, string? currency, bool includeMarker)
    {
        var definition = Currencies.Get(currency);
        var format = Current().CurrencyFormats.SingleOrDefault(item => item.CurrencyCode == definition.Code) ?? DefaultFormat(definition);
        var info = new System.Globalization.NumberFormatInfo { NumberDecimalDigits = format.DecimalPlaces, NumberDecimalSeparator = format.DecimalSeparator, NumberGroupSeparator = format.ThousandsSeparator };
        var number = amount.ToString($"N{format.DecimalPlaces}", info);
        if (!includeMarker) return number;
        var marker = format.CurrencyDisplay == CurrencyDisplay.Symbol ? definition.Symbol : definition.Code;
        var space = format.UseSpace ? " " : string.Empty;
        return format.CurrencyPosition == CurrencyPosition.Before ? marker + space + number : number + space + marker;
    }

    private UserPreference Current() => cachedUser ??= GetUserAsync().GetAwaiter().GetResult();
    private static UserCurrencyFormat DefaultFormat(CurrencyDefinition definition) => new() { CurrencyCode = definition.Code, DecimalPlaces = definition.DecimalDigits, DecimalSeparator = ",", ThousandsSeparator = " ", CurrencyDisplay = CurrencyDisplay.Code, CurrencyPosition = CurrencyPosition.After, UseSpace = true };
}
