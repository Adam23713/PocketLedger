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

    private static decimal GetSignedAmount(Transaction transaction) => TransactionSemantics.Resolve(transaction.Type, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.DebtOperationType).SourceAccountChange;

    private static decimal GetSignedAmount(Guid accountId, Transaction transaction)
    {
        var semantics = TransactionSemantics.Resolve(transaction.Type, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.DebtOperationType);
        if (transaction.AccountId == accountId) return semantics.SourceAccountChange;
        if (transaction.TargetAccountId == accountId) return semantics.TargetAccountChange;
        return 0;
    }
}
