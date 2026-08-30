using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Statistics;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class StatisticsController(IStatisticsService statisticsService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, string? currency, CancellationToken cancellationToken)
    {
        var today = await userContext.TodayAsync(cancellationToken);
        var selectedYear = year ?? today.Year;
        var selectedMonth = month ?? today.Month;
        try
        {
            var availableCurrencyCodes = await statisticsService.GetAvailableCurrenciesAsync(selectedYear, selectedMonth, cancellationToken);
            var defaultCurrency = (await userContext.GetUserAsync(cancellationToken)).DefaultCurrency;
            currency = availableCurrencyCodes.Contains(currency ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                ? currency!.ToUpperInvariant()
                : availableCurrencyCodes.Contains(defaultCurrency, StringComparer.OrdinalIgnoreCase) ? defaultCurrency : availableCurrencyCodes.FirstOrDefault() ?? defaultCurrency;
            var summary = await statisticsService.GetSummaryAsync(selectedYear, selectedMonth, currency, cancellationToken);
            return View(new StatisticsViewModel
            {
                Year = selectedYear,
                Month = selectedMonth,
                Currency = currency,
                AvailableCurrencies = Currencies.All.Where(definition => availableCurrencyCodes.Contains(definition.Code)).ToList(),
                Income = summary.Income,
                Expenses = summary.Expenses,
                Savings = summary.Savings,
                Balance = summary.Balance,
                IncomeByCategory = summary.IncomeByCategory.Select(item => ToCategoryViewModel(item)).ToList(),
                ExpenseByCategory = summary.ExpenseByCategory.Select(item => ToCategoryViewModel(item)).ToList(),
                ExpenseMainCategories = summary.ExpenseMainCategories.Select(item =>
                {
                    var icon = TransactionIcons.ResolveCategoryIcon(item.Icon, Models.Enums.CategoryType.Expense);
                    return new StatisticsMainCategoryViewModel(item.CategoryId, item.Name, item.Amount, icon.WebPath, icon.DisplayName, item.Subcategories.Select(subcategory => new StatisticsSubcategoryViewModel(subcategory.CategoryId, subcategory.Name, subcategory.Amount)).ToList());
                }).ToList(),
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
        var icon = TransactionIcons.ResolveCategoryIcon(item.Icon, item.CategoryType);
        return new StatisticsCategoryViewModel(item.Name, item.Amount, icon.WebPath, icon.DisplayName);
    }
}
