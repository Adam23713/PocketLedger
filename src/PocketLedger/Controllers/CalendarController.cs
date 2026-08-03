using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.ViewModels.Calendar;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class CalendarController(ICalendarService calendarService) : Controller
{
    public async Task<IActionResult> Index(int? year, int? month, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
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
                    Income = summary?.Income ?? 0,
                    Expenses = summary?.Expenses ?? 0,
                    Balance = summary?.Balance ?? 0
                });
            }

            return View(new CalendarViewModel
            {
                Year = selected.Year,
                Month = selected.Month,
                Today = today,
                MonthlyIncome = summaries.Values.Sum(summary => summary.Income),
                MonthlyExpenses = summaries.Values.Sum(summary => summary.Expenses),
                Days = days
            });
        }
        catch (BusinessRuleException)
        {
            return RedirectToAction(nameof(Index));
        }
    }
}
