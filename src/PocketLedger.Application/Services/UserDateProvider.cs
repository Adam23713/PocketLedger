using PocketLedger.Models;

namespace PocketLedger.Services;

public interface IUserDateProvider
{
    string NormalizeTimeZoneId(string timeZoneId);
    DateOnly Today(string timeZoneId);
    DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId);
    TimeOnly LocalTime(string timeZoneId);
}

public sealed class UserDateProvider(TimeProvider clock) : IUserDateProvider
{
    public string NormalizeTimeZoneId(string timeZoneId) => GetTimeZone(timeZoneId).Id;
    public DateOnly Today(string timeZoneId) => DateOnly.FromDateTime(LocalNow(timeZoneId).DateTime);

    public DateTimeOffset ToUtc(DateOnly date, TimeOnly time, string timeZoneId)
    {
        var zone = GetTimeZone(timeZoneId);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) throw new BusinessRuleException("The selected local time does not exist because of a daylight-saving transition.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    public TimeOnly LocalTime(string timeZoneId) => TimeOnly.FromDateTime(LocalNow(timeZoneId).DateTime);

    private DateTimeOffset LocalNow(string timeZoneId) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), GetTimeZone(timeZoneId));

    private static TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        try { return UserTimeZones.Get(timeZoneId); }
        catch (ArgumentException exception) { throw new BusinessRuleException(exception.Message); }
    }
}

public sealed class UserDateOptions
{
    public const string SectionName = "UserDates";
    public string DefaultTimeZoneId { get; set; } = "UTC";
}
