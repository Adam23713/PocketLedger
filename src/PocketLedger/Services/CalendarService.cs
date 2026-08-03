using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class CalendarService(PocketLedgerDbContext dbContext) : ICalendarService
{
    public async Task<IReadOnlyDictionary<DateOnly, CalendarDaySummary>> GetMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (year < 1 || month is < 1 or > 12) throw new BusinessRuleException("The selected month is invalid.");
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var transactions = await dbContext.Transactions.AsNoTracking()
            .Where(transaction => transaction.TransactionDate >= start && transaction.TransactionDate < end && transaction.Type != TransactionType.Transfer)
            .Select(transaction => new Models.Entities.Transaction { TransactionDate = transaction.TransactionDate, Type = transaction.Type, Amount = transaction.Amount, AdjustmentDirection = transaction.AdjustmentDirection })
            .ToListAsync(cancellationToken);

        return transactions.GroupBy(transaction => transaction.TransactionDate).ToDictionary(group => group.Key, group =>
        {
            var totals = PeriodCalculator.Calculate(group);
            return new CalendarDaySummary(group.Key, totals.Income, totals.Expenses, totals.Balance, group.Count());
        });
    }
}
