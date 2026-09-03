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
        var items = transactions.Select(transaction => (transaction.Amount, Semantics: TransactionSemantics.Resolve(transaction.Type, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.DebtOperationType))).ToList();
        return new PeriodTotals(
            items.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Income).Sum(item => item.Amount),
            items.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Expense).Sum(item => item.Amount),
            items.Where(item => item.Semantics.ReportingClassification is TransactionReportingClassification.AdjustmentIncrease or TransactionReportingClassification.AdjustmentDecrease).Sum(item => item.Semantics.SourceAccountChange));
    }
}
