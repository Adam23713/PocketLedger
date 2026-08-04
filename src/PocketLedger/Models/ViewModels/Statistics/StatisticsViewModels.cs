namespace PocketLedger.Models.ViewModels.Statistics;

public class StatisticsViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
    public decimal Savings { get; init; }
    public decimal Balance { get; init; }
    public IReadOnlyList<StatisticsCategoryViewModel> IncomeByCategory { get; init; } = [];
    public IReadOnlyList<StatisticsCategoryViewModel> ExpenseByCategory { get; init; } = [];
    public IReadOnlyList<StatisticsAccountViewModel> AccountBalances { get; init; } = [];
    public IReadOnlyList<StatisticsTrendViewModel> MonthlyTrend { get; init; } = [];
    public IReadOnlyList<StatisticsRecurringExpenseViewModel> RecurringExpenses { get; init; } = [];
}

public record StatisticsCategoryViewModel(string Name, decimal Amount, string IconPath, string IconAlt);
public record StatisticsAccountViewModel(Guid Id, string Name, string Currency, decimal Balance);
public record StatisticsTrendViewModel(int Year, int Month, decimal Income, decimal Expenses, decimal Savings, decimal Balance);
public record StatisticsRecurringExpenseViewModel(string MainCategoryName, string IconPath, string IconAlt, string Currency, int OccurrenceCount, decimal Amount);
