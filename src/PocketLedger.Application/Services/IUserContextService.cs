using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public interface IUserContextService
{
    Task<UserPreference> GetUserAsync(CancellationToken cancellationToken = default);
    Task<DateTimeOffset> ToUtcAsync(DateOnly date, TimeOnly time, CancellationToken cancellationToken = default);
    Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default);
    Task<string> FormatMoneyAsync(decimal amount, string currency, CancellationToken cancellationToken = default);
    string Format(decimal amount, string? currency);
    string FormatNumber(decimal amount, string? currency);
    MoneyInputFormat GetMoneyInputFormat(string currency);
}

public sealed record MoneyInputFormat(int DecimalPlaces, string DecimalSeparator, string ThousandsSeparator, string CurrencyMarker = "", CurrencyPosition CurrencyPosition = CurrencyPosition.After, bool UseSpace = true);
