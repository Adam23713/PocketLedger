using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class RecurringSchedule
{
    public static DateOnly AddOccurrences(DateOnly firstOccurrence, RecurringFrequency frequency, int occurrenceOffset)
    {
        if (occurrenceOffset < 0) throw new ArgumentOutOfRangeException(nameof(occurrenceOffset));
        return frequency switch
        {
            RecurringFrequency.Daily => firstOccurrence.AddDays(occurrenceOffset),
            RecurringFrequency.Weekly => firstOccurrence.AddDays(occurrenceOffset * 7),
            RecurringFrequency.Monthly => AddMonthsKeepingScheduleDay(firstOccurrence, occurrenceOffset),
            RecurringFrequency.Yearly => AddYearsKeepingScheduleDay(firstOccurrence, occurrenceOffset),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };
    }

    public static decimal ToMonthlyAmount(decimal amount, RecurringFrequency frequency) => frequency switch
    {
        RecurringFrequency.Daily => amount * 365m / 12m,
        RecurringFrequency.Weekly => amount * 52m / 12m,
        RecurringFrequency.Monthly => amount,
        RecurringFrequency.Yearly => amount / 12m,
        _ => throw new ArgumentOutOfRangeException(nameof(frequency))
    };

    public static DateOnly? GetNextOccurrence(RecurringTransaction template, DateOnly from)
    {
        var candidate = from > template.FirstOccurrence ? from : template.FirstOccurrence;
        var searchEnd = template.LastOccurrence ?? candidate.AddYears(2);
        for (var date = candidate; date <= searchEnd; date = date.AddDays(1))
        {
            if (IsOccurrence(template, date)) return date;
        }

        return null;
    }

    public static IReadOnlyList<DateOnly> GetOccurrences(RecurringTransaction template, DateOnly from, DateOnly to)
    {
        var start = from > template.FirstOccurrence ? from : template.FirstOccurrence;
        var end = template.LastOccurrence is { } last && last < to ? last : to;
        if (start > end) return [];

        var occurrences = new List<DateOnly>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (IsOccurrence(template, date)) occurrences.Add(date);
        }
        return occurrences;
    }

    public static bool IsOccurrence(RecurringTransaction template, DateOnly date)
    {
        if (date < template.FirstOccurrence || template.LastOccurrence is { } last && date > last) return false;
        var first = template.FirstOccurrence;
        return template.Frequency switch
        {
            RecurringFrequency.Daily => true,
            RecurringFrequency.Weekly => date.DayNumber % 7 == first.DayNumber % 7,
            RecurringFrequency.Monthly => date.Day == Math.Min(first.Day, DateTime.DaysInMonth(date.Year, date.Month)),
            RecurringFrequency.Yearly => date.Month == first.Month && date.Day == Math.Min(first.Day, DateTime.DaysInMonth(date.Year, first.Month)),
            _ => false
        };
    }

    private static DateOnly AddMonthsKeepingScheduleDay(DateOnly first, int months)
    {
        var month = new DateOnly(first.Year, first.Month, 1).AddMonths(months);
        return new DateOnly(month.Year, month.Month, Math.Min(first.Day, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    private static DateOnly AddYearsKeepingScheduleDay(DateOnly first, int years)
    {
        var year = first.Year + years;
        return new DateOnly(year, first.Month, Math.Min(first.Day, DateTime.DaysInMonth(year, first.Month)));
    }
}
