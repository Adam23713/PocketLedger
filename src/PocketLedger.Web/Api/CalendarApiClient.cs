using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class CalendarApiClient(HttpClient client) : ApiClientBase(client), ICalendarService
{
    public Task<IReadOnlyDictionary<DateOnly, CalendarDaySummary>> GetMonthAsync(int year, int month, CancellationToken token) => GetAsync<IReadOnlyDictionary<DateOnly, CalendarDaySummary>>($"api/v1/calendar/{year}/{month}", token);
}
