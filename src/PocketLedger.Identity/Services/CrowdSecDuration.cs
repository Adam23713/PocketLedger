using System.Globalization;
using System.Text.RegularExpressions;

namespace PocketLedger.Services;

// CrowdSec reports remaining ban time as a Go duration string, for example "3h59m12.5s".
public static partial class CrowdSecDuration
{
    public static TimeSpan? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        var negative = text.StartsWith('-');
        if (negative) text = text[1..];
        var matches = Part().Matches(text);
        if (matches.Count == 0 || string.Concat(matches.Select(match => match.Value)) != text) return null;
        var milliseconds = 0d;
        foreach (Match match in matches)
        {
            var amount = double.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
            milliseconds += match.Groups["unit"].Value switch
            {
                "h" => amount * 3_600_000, "m" => amount * 60_000, "s" => amount * 1_000, "ms" => amount,
                "us" or "µs" => amount / 1_000, _ => amount / 1_000_000
            };
        }
        return TimeSpan.FromMilliseconds(negative ? -milliseconds : milliseconds);
    }

    public static string Format(TimeSpan value)
    {
        if (value <= TimeSpan.Zero) return "Expiring";
        if (value.TotalDays >= 1) return $"{(int)value.TotalDays}d {value.Hours}h {value.Minutes}m";
        if (value.TotalHours >= 1) return $"{value.Hours}h {value.Minutes}m";
        return $"{value.Minutes}m {value.Seconds}s";
    }

    [GeneratedRegex(@"(?<amount>\d+(?:\.\d+)?)(?<unit>h|ms|m|s|us|µs|ns)")]
    private static partial Regex Part();
}
