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
            .Select(transaction => new { transaction.TransactionDate, transaction.Type, transaction.Amount, transaction.AdjustmentDirection, Currency = transaction.SourceCurrency })
            .ToListAsync(cancellationToken);

        return transactions.GroupBy(transaction => transaction.TransactionDate).ToDictionary(group => group.Key, group =>
        {
            var totals = group.GroupBy(item => item.Currency).Select(currencyGroup =>
            {
                var income = currencyGroup.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount);
                var expenses = currencyGroup.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount);
                var adjustments = currencyGroup.Where(item => item.Type == TransactionType.Adjustment).Sum(item => item.AdjustmentDirection == AdjustmentDirection.Increase ? item.Amount : -item.Amount);
                return new CurrencyPeriodTotal(currencyGroup.Key, income, expenses, income - expenses + adjustments);
            }).ToList();
            return new CalendarDaySummary(group.Key, totals, group.Count());
        });
    }
}
