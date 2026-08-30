using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class TransactionRules
{
    public static void Validate(Transaction transaction, Account? account, Category? category)
    {
        if (account is null)
        {
            throw new BusinessRuleException("The selected account does not exist.");
        }

        if (transaction.Amount <= 0)
        {
            throw new BusinessRuleException("Amount must be greater than zero.");
        }

        if (transaction.Type is not (TransactionType.Income or TransactionType.Expense or TransactionType.Adjustment))
        {
            throw new BusinessRuleException("Transfers are not supported yet.");
        }

        if (transaction.TargetAccountId is not null || transaction.TargetAmount is not null)
        {
            throw new BusinessRuleException("Target account and target amount must be empty for non-transfer transactions.");
        }

        if (transaction.Type == TransactionType.Adjustment)
        {
            if (transaction.CategoryId is not null || category is not null)
            {
                throw new BusinessRuleException("Adjustments cannot use a category.");
            }

            if (transaction.AdjustmentDirection is null)
            {
                throw new BusinessRuleException("Adjustment direction is required.");
            }

            return;
        }

        if (transaction.AdjustmentDirection is not null)
        {
            throw new BusinessRuleException("Adjustment direction is only valid for adjustments.");
        }

        if (category is null)
        {
            throw new BusinessRuleException("A category is required for income and expense transactions.");
        }

        var expectedCategoryType = transaction.Type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense;
        if (category.Type != expectedCategoryType)
        {
            throw new BusinessRuleException($"A {transaction.Type.ToString().ToLowerInvariant()} transaction requires a {expectedCategoryType.ToString().ToLowerInvariant()} category.");
        }
    }

    public static void ValidateTransfer(Transaction transaction, Account? sourceAccount, Account? targetAccount)
    {
        if (sourceAccount is null || targetAccount is null)
        {
            throw new BusinessRuleException("Source and target accounts are required.");
        }

        if (sourceAccount.Id == targetAccount.Id)
        {
            throw new BusinessRuleException("Source and target accounts must be different.");
        }

        if (transaction.Amount <= 0)
        {
            throw new BusinessRuleException("Amount must be greater than zero.");
        }

        if (transaction.CategoryId is not null || transaction.Category is not null)
        {
            throw new BusinessRuleException("Transfers cannot use a category.");
        }

        if (transaction.AdjustmentDirection is not null)
        {
            throw new BusinessRuleException("Transfers cannot use an adjustment direction.");
        }

        if (sourceAccount.Currency == targetAccount.Currency && transaction.TargetAmount != transaction.Amount)
        {
            throw new BusinessRuleException("Target amount must equal source amount when account currencies match.");
        }

        if (transaction.TargetAmount is <= 0)
        {
            throw new BusinessRuleException("Target amount must be greater than zero.");
        }

        if (transaction.ExchangeRate is <= 0) throw new BusinessRuleException("Exchange rate must be greater than zero.");
    }
}
