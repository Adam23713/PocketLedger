using PocketLedger.Models.Enums;

namespace PocketLedger.Services.Interfaces;

public record StatisticsCategoryTotal(Guid? CategoryId, string Name, decimal Amount, string? Icon, CategoryType CategoryType);
public record StatisticsMainCategoryTotal(Guid? CategoryId, string Name, decimal Amount, string? Icon, IReadOnlyList<StatisticsSubcategoryTotal> Subcategories);
public record StatisticsSubcategoryTotal(Guid? CategoryId, string Name, decimal Amount);
public record StatisticsAccountBalance(Guid AccountId, string Name, string Currency, decimal Balance);
public record StatisticsMonthlyTrend(int Year, int Month, decimal Income, decimal Expenses, decimal Savings, decimal Balance);
public record StatisticsRecurringExpense(string MainCategoryName, string? MainCategoryIcon, string Currency, int OccurrenceCount, decimal Amount);
public record StatisticsSummary(decimal Income, decimal Expenses, decimal Savings, decimal Balance, IReadOnlyList<StatisticsCategoryTotal> IncomeByCategory, IReadOnlyList<StatisticsCategoryTotal> ExpenseByCategory, IReadOnlyList<StatisticsMainCategoryTotal> ExpenseMainCategories, IReadOnlyList<StatisticsAccountBalance> AccountBalances, IReadOnlyList<StatisticsMonthlyTrend> MonthlyTrend, IReadOnlyList<StatisticsRecurringExpense> RecurringExpenses);

public interface IStatisticsService
{
    Task<StatisticsSummary> GetSummaryAsync(int year, int month, CancellationToken cancellationToken);
}
