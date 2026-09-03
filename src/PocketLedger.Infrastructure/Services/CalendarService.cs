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
            .Select(transaction => new { transaction.TransactionDate, transaction.Type, transaction.Amount, transaction.AdjustmentDirection, transaction.DebtOperationType, Currency = transaction.SourceCurrency })
            .ToListAsync(cancellationToken);

        return transactions.GroupBy(transaction => transaction.TransactionDate).ToDictionary(group => group.Key, group =>
        {
            var totals = group.GroupBy(item => item.Currency).Select(currencyGroup =>
            {
                // Calendar cash-flow intentionally presents adjustments on the income/expense sides.
                var classified = currencyGroup.Select(item => (item.Amount, Classification: TransactionSemantics.Resolve(item.Type, item.Amount, adjustmentDirection: item.AdjustmentDirection, debtOperationType: item.DebtOperationType).ReportingClassification)).ToList();
                var income = classified.Where(item => item.Classification is TransactionReportingClassification.Income or TransactionReportingClassification.AdjustmentIncrease).Sum(item => item.Amount);
                var expenses = classified.Where(item => item.Classification is TransactionReportingClassification.Expense or TransactionReportingClassification.AdjustmentDecrease).Sum(item => item.Amount);
                return new CurrencyPeriodTotal(currencyGroup.Key, income, expenses, income - expenses);
            }).ToList();
            return new CalendarDaySummary(group.Key, totals, group.Count());
        });
    }
}
