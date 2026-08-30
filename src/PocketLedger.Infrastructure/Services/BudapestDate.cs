namespace PocketLedger.Services;

public static class BudapestDate
{
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Budapest");

    public static DateOnly Today(TimeProvider timeProvider)
    {
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }
}
