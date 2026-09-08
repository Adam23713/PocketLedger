using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public static class PlannerRules
{
    public static void Validate(PlannerItemInput input, Account? account, Account? target, Category? category)
    {
        if (input.Month.Day != 1 || input.Month.Year is < 2 or > 9998) throw new BusinessRuleException("Select a valid planning month.");
        if (input.PlannedDate is { } date && (date.Year != input.Month.Year || date.Month != input.Month.Month)) throw new BusinessRuleException("The planned date must belong to the selected month.");
        if (input.Type is not (TransactionType.Income or TransactionType.Expense or TransactionType.Transfer)) throw new BusinessRuleException("Select Income, Expense or Transfer.");
        if (input.Note?.Length > 500) throw new BusinessRuleException("Note cannot exceed 500 characters.");
        ValidateAmount(input.Amount);
        ValidateAmount(input.AccountAmount);
        if (input.TargetAmount is { } targetAmount) ValidateAmount(targetAmount);
        var currency = AccountRules.NormalizeAndValidateCurrency(input.Currency);
        if (account is null) throw new BusinessRuleException("The selected account does not exist.");
        if (currency == account.Currency && input.Amount != input.AccountAmount) throw new BusinessRuleException("The account amount must equal the original amount when currencies match.");
        var transaction = new Transaction { Type = input.Type, AccountId = input.AccountId, TargetAccountId = input.TargetAccountId, CategoryId = input.CategoryId, Amount = input.AccountAmount, TargetAmount = input.TargetAmount };
        if (input.Type == TransactionType.Transfer)
        {
            if (input.PlannedDate is null) throw new BusinessRuleException("A planned date is required for transfers.");
            if (currency != account.Currency) throw new BusinessRuleException("A transfer's source amount must use the source account currency.");
            if (input.TargetAmount is null) throw new BusinessRuleException("Enter the amount credited to the target account.");
            TransactionRules.ValidateTransfer(transaction, account, target);
        }
        else
        {
            TransactionRules.Validate(transaction, account, category);
        }
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0 || amount > 999999999999999.9999m || decimal.Round(amount, 4) != amount)
            throw new BusinessRuleException("Amounts must be positive, at most 999999999999999.9999, with at most four decimal places.");
    }
}
