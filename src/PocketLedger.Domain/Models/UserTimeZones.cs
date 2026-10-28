namespace PocketLedger.Models;

public sealed record UserTimeZoneOption(string Id, string DisplayName);

public static class UserTimeZones
{
    // IANA time zones known to the host, for pickers. Windows ids are converted, and legacy aliases (no region prefix) are left out.
    public static IReadOnlyList<UserTimeZoneOption> All { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(zone => (Zone: zone, Id: zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null))
        .Where(item => item.Id is not null && (item.Id == "UTC" || item.Id.Contains('/') && !item.Id.StartsWith("Etc/", StringComparison.Ordinal) && char.IsUpper(item.Id[0]) && !item.Id.StartsWith("US/", StringComparison.Ordinal)))
        .GroupBy(item => item.Id!, StringComparer.Ordinal)
        .Select(group => group.First())
        .Select(item => new UserTimeZoneOption(item.Id!, $"{item.Id!.Replace('_', ' ')} (UTC{FormatOffset(item.Zone.BaseUtcOffset)})"))
        .OrderBy(item => item.Id, StringComparer.Ordinal)
        .ToList();

    private static string FormatOffset(TimeSpan offset) => (offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm");

    public static TimeZoneInfo Get(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("The selected time zone is not available.", nameof(id)); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("The selected time zone is invalid.", nameof(id)); }
    }
}
