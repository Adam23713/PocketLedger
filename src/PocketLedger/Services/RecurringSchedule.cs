using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class RecurringSchedule
{
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
}
