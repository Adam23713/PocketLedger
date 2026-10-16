using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Calendar;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class CalendarController(ICalendarService calendarService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, string? currency, CancellationToken cancellationToken)
    {
        var today = await userContext.TodayAsync(cancellationToken);
        var selected = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        try
        {
            var summaries = await calendarService.GetMonthAsync(selected.Year, selected.Month, cancellationToken);
            var monthlyTotals = summaries.Values.SelectMany(summary => summary.Totals).GroupBy(total => total.Currency).Select(group => new CalendarCurrencyTotalViewModel(group.Key, group.Sum(item => item.Income), group.Sum(item => item.Expenses), group.Sum(item => item.Balance))).OrderBy(total => total.Currency).ToList();
            var defaultCurrency = (await userContext.GetUserAsync(cancellationToken)).DefaultCurrency;
            var activeCurrency = monthlyTotals.Any(total => total.Currency == currency) ? currency! : monthlyTotals.Any(total => total.Currency == defaultCurrency) ? defaultCurrency : monthlyTotals.FirstOrDefault()?.Currency ?? defaultCurrency;
            var gridStart = selected.AddDays(-(((int)selected.DayOfWeek + 6) % 7));
            var monthEnd = selected.AddMonths(1).AddDays(-1);
            var gridEnd = monthEnd.AddDays(6 - (((int)monthEnd.DayOfWeek + 6) % 7));
            var days = new List<CalendarDayViewModel>();
            for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
            {
                summaries.TryGetValue(date, out var summary);
                days.Add(new CalendarDayViewModel
                {
                    Date = date,
                    IsCurrentMonth = date.Month == selected.Month,
                    IsToday = date == today,
                    TransactionCount = summary?.Totals.Any(total => total.Currency == activeCurrency) == true ? summary.TransactionCount : 0,
                    Totals = summary?.Totals.Where(total => total.Currency == activeCurrency).Select(total => new CalendarCurrencyTotalViewModel(total.Currency, total.Income, total.Expenses, total.Balance)).ToList() ?? []
                });
            }
            var activeMonthlyTotal = monthlyTotals.FirstOrDefault(total => total.Currency == activeCurrency) ?? new CalendarCurrencyTotalViewModel(activeCurrency, 0, 0, 0);
            var weeklyTotals = days.Chunk(7).Select(week =>
            {
                var totals = week.Where(day => day.IsCurrentMonth).SelectMany(day => day.Totals).ToList();
                var income = totals.Sum(total => total.Income);
                var expenses = totals.Sum(total => total.Expenses);
                return new CalendarWeekTotalViewModel(week[0].Date < selected ? selected : week[0].Date, week[^1].Date > monthEnd ? monthEnd : week[^1].Date, income, expenses, income - expenses);
            }).ToList();

            return View(new CalendarViewModel
            {
                Year = selected.Year,
                Month = selected.Month,
                Today = today,
                ActiveCurrency = activeCurrency,
                AvailableCurrencies = Currencies.All.Where(definition => monthlyTotals.Any(total => total.Currency == definition.Code)).ToList(),
                MonthlyTotals = monthlyTotals.Count > 0 ? monthlyTotals : [new CalendarCurrencyTotalViewModel(defaultCurrency, 0, 0, 0)],
                ActiveMonthlyTotal = activeMonthlyTotal,
                WeeklyTotals = weeklyTotals,
                Days = days
            });
        }
        catch (BusinessRuleException)
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
