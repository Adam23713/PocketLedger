namespace PocketLedger.Models;

public sealed record CurrencyDefinition(string Code, string DisplayName, string Symbol, int DecimalDigits);

public static class Currencies
{
    public static IReadOnlyList<CurrencyDefinition> All { get; } =
    [
        new("HUF", "Hungarian Forint", "Ft", 0),
        new("EUR", "Euro", "€", 2),
        new("USD", "US Dollar", "$", 2)
    ];

    public static CurrencyDefinition Get(string? code) => All.SingleOrDefault(item => string.Equals(item.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("Unsupported currency.", nameof(code));

    public static bool Exists(string? code) => All.Any(item => string.Equals(item.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase));
}
