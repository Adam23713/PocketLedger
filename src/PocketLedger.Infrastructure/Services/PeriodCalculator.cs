using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public record PeriodTotals(decimal Income, decimal Expenses, decimal Adjustments)
{
    public decimal Savings => Income - Expenses;
    public decimal Balance => Income - Expenses + Adjustments;
}

public static class PeriodCalculator
{
    public static PeriodTotals Calculate(IEnumerable<Transaction> transactions)
    {
        var items = transactions.Where(transaction => transaction.Type != TransactionType.Transfer).ToList();
        return new PeriodTotals(
            items.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount),
            items.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount),
            items.Where(item => item.Type == TransactionType.Adjustment).Sum(item => item.AdjustmentDirection == AdjustmentDirection.Increase ? item.Amount : -item.Amount));
    }
}
