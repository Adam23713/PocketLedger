using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Statistics;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class StatisticsController(IStatisticsService statisticsService) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        var selectedYear = year ?? today.Year;
        var selectedMonth = month ?? today.Month;
        try
        {
            var summary = await statisticsService.GetSummaryAsync(selectedYear, selectedMonth, cancellationToken);
            return View(new StatisticsViewModel
            {
                Year = selectedYear,
                Month = selectedMonth,
                Income = summary.Income,
                Expenses = summary.Expenses,
                Savings = summary.Savings,
                Balance = summary.Balance,
                IncomeByCategory = summary.IncomeByCategory.Select(item => ToCategoryViewModel(item)).ToList(),
                ExpenseByCategory = summary.ExpenseByCategory.Select(item => ToCategoryViewModel(item)).ToList(),
                AccountBalances = summary.AccountBalances.Select(item => new StatisticsAccountViewModel(item.AccountId, item.Name, item.Currency, item.Balance)).ToList(),
                MonthlyTrend = summary.MonthlyTrend.Select(item => new StatisticsTrendViewModel(item.Year, item.Month, item.Income, item.Expenses, item.Savings, item.Balance)).ToList(),
                RecurringExpenses = summary.RecurringExpenses.Select(item =>
                {
                    var icon = CategoryIcons.Resolve(item.MainCategoryIcon, Models.Enums.CategoryType.Expense);
                    return new StatisticsRecurringExpenseViewModel(item.MainCategoryName, icon.WebPath, icon.DisplayName, item.Currency, item.OccurrenceCount, item.Amount);
                }).ToList()
            });
        }
        catch (BusinessRuleException)
        {
            return RedirectToAction(nameof(Index));
        }
    }

    private static StatisticsCategoryViewModel ToCategoryViewModel(StatisticsCategoryTotal item)
    {
        var icon = CategoryIcons.Resolve(item.Icon, item.CategoryType);
        return new StatisticsCategoryViewModel(item.Name, item.Amount, icon.WebPath, icon.DisplayName);
    }
}
