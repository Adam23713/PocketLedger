using System.Globalization;

namespace PocketLedger.Helpers;

public static class MoneyFormatter
{
    private static readonly NumberFormatInfo HufNumberFormat = new()
    {
        NumberGroupSeparator = " ",
        NumberDecimalDigits = 0
    };

    public static string Format(decimal amount, string? currency)
    {
        return string.Equals(currency?.Trim(), "HUF", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("N0", HufNumberFormat)
            : amount.ToString("N2");
    }
}
