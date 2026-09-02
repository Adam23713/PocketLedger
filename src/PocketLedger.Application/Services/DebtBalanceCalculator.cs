using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public static class DebtBalanceCalculator
{
    public static decimal Calculate(decimal originalAmount, IEnumerable<Transaction> operations, Guid? excludingTransactionId = null)
    {
        return operations
            .Where(operation => operation.Id != excludingTransactionId && operation.DebtOperationType is not null)
            .Aggregate(originalAmount, (balance, operation) => balance + DebtRules.GetDebtDelta(operation.DebtOperationType!.Value, operation.Amount));
    }
}
