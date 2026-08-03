namespace PocketLedger.Services.Interfaces;

public record CalendarDaySummary(DateOnly Date, decimal Income, decimal Expenses, decimal Balance, int TransactionCount);

public interface ICalendarService
{
    Task<IReadOnlyDictionary<DateOnly, CalendarDaySummary>> GetMonthAsync(int year, int month, CancellationToken cancellationToken);
}
