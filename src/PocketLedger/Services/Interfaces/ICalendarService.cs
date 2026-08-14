namespace PocketLedger.Services.Interfaces;

public record CurrencyPeriodTotal(string Currency, decimal Income, decimal Expenses, decimal Balance);
public record CalendarDaySummary(DateOnly Date, IReadOnlyList<CurrencyPeriodTotal> Totals, int TransactionCount);

public interface ICalendarService
{
    Task<IReadOnlyDictionary<DateOnly, CalendarDaySummary>> GetMonthAsync(int year, int month, CancellationToken cancellationToken);
}
