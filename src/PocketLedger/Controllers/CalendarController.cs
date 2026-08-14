using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.ViewModels.Calendar;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class CalendarController(ICalendarService calendarService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
    {
        var today = await userContext.TodayAsync(cancellationToken);
        var selected = new DateOnly(year ?? today.Year, month ?? today.Month, 1);
        try
        {
            var summaries = await calendarService.GetMonthAsync(selected.Year, selected.Month, cancellationToken);
            var gridStart = selected.AddDays(-(((int)selected.DayOfWeek + 6) % 7));
            var gridEnd = selected.AddMonths(1).AddDays(6 - (((int)selected.AddMonths(1).AddDays(-1).DayOfWeek + 6) % 7));
            var days = new List<CalendarDayViewModel>();
            for (var date = gridStart; date <= gridEnd; date = date.AddDays(1))
            {
                summaries.TryGetValue(date, out var summary);
                days.Add(new CalendarDayViewModel
                {
                    Date = date,
                    IsCurrentMonth = date.Month == selected.Month,
                    IsToday = date == today,
                    TransactionCount = summary?.TransactionCount ?? 0,
                    Totals = summary?.Totals.Select(total => new CalendarCurrencyTotalViewModel(total.Currency, total.Income, total.Expenses, total.Balance)).ToList() ?? []
                });
            }

            return View(new CalendarViewModel
            {
                Year = selected.Year,
                Month = selected.Month,
                Today = today,
                MonthlyTotals = summaries.Values.SelectMany(summary => summary.Totals).GroupBy(total => total.Currency).Select(group => new CalendarCurrencyTotalViewModel(group.Key, group.Sum(item => item.Income), group.Sum(item => item.Expenses), group.Sum(item => item.Balance))).ToList(),
                Days = days
            });
        }
        catch (BusinessRuleException)
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
