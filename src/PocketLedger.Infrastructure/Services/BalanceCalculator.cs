using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

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

    public static IReadOnlyList<CurrencyBalance> CalculateMainBalance(IEnumerable<(string Currency, decimal Balance, bool IncludeInMainBalance)> accounts)
    {
        return accounts.Where(account => account.IncludeInMainBalance)
            .GroupBy(account => account.Currency)
            .Select(group => new CurrencyBalance(group.Key, group.Sum(account => account.Balance)))
            .OrderBy(balance => balance.Currency)
            .ToList();
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
