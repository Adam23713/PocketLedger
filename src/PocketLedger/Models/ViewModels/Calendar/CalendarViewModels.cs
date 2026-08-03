namespace PocketLedger.Models.ViewModels.Calendar;

public class CalendarViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public DateOnly Today { get; init; }
    public decimal MonthlyIncome { get; init; }
    public decimal MonthlyExpenses { get; init; }
    public decimal MonthlyTotal => MonthlyIncome - MonthlyExpenses;
    public IReadOnlyList<CalendarDayViewModel> Days { get; init; } = [];
}

public class CalendarDayViewModel
{
    public DateOnly Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public int TransactionCount { get; init; }
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Balance { get; init; }
}
