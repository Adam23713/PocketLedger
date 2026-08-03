using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class BalanceCalculator
{
    public static decimal Calculate(decimal initialBalance, IEnumerable<Transaction> transactions)
    {
        return transactions.Aggregate(initialBalance, (balance, transaction) => balance + GetSignedAmount(transaction));
    }

    public static decimal Calculate(Guid accountId, decimal initialBalance, IEnumerable<Transaction> transactions)
    {
        return transactions.Aggregate(initialBalance, (balance, transaction) => balance + GetSignedAmount(accountId, transaction));
    }

    public static decimal CalculateMainBalance(IEnumerable<(decimal Balance, bool IncludeInMainBalance)> accounts)
    {
        return accounts.Where(account => account.IncludeInMainBalance).Sum(account => account.Balance);
    }

    private static decimal GetSignedAmount(Transaction transaction) => transaction.Type switch
    {
        TransactionType.Income => transaction.Amount,
        TransactionType.Expense => -transaction.Amount,
        TransactionType.Adjustment when transaction.AdjustmentDirection == AdjustmentDirection.Increase => transaction.Amount,
        TransactionType.Adjustment when transaction.AdjustmentDirection == AdjustmentDirection.Decrease => -transaction.Amount,
        _ => 0
    };

    private static decimal GetSignedAmount(Guid accountId, Transaction transaction)
    {
        if (transaction.Type == TransactionType.Transfer)
        {
            if (transaction.AccountId == accountId)
            {
                return -transaction.Amount;
            }

            if (transaction.TargetAccountId == accountId)
            {
                return transaction.TargetAmount ?? transaction.Amount;
            }

            return 0;
        }

        return transaction.AccountId == accountId ? GetSignedAmount(transaction) : 0;
    }
}
