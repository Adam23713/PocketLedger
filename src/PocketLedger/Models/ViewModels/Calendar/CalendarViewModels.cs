namespace PocketLedger.Models.ViewModels.Calendar;

public class CalendarViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public DateOnly Today { get; init; }
    public IReadOnlyList<CalendarCurrencyTotalViewModel> MonthlyTotals { get; init; } = [];
    public IReadOnlyList<CalendarDayViewModel> Days { get; init; } = [];
}

public class CalendarDayViewModel
{
    public DateOnly Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public int TransactionCount { get; init; }
    public IReadOnlyList<CalendarCurrencyTotalViewModel> Totals { get; init; } = [];
}

public record CalendarCurrencyTotalViewModel(string Currency, decimal Income, decimal Expenses, decimal Balance);
