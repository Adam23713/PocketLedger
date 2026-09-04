namespace PocketLedger.Models;

public static class UserTimeZones
{
    public static TimeZoneInfo Get(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("The selected time zone is not available.", nameof(id)); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("The selected time zone is invalid.", nameof(id)); }
    }
}
