using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public static class RecurringTransactionRules
{
    public static void Validate(RecurringTransaction template, Account? account, Category? category)
    {
        ValidateSchedule(template);

        TransactionRules.Validate(new Transaction
        {
            Type = template.Type,
            AccountId = template.AccountId,
            Account = account!,
            Amount = template.Amount,
            AdjustmentDirection = template.AdjustmentDirection,
            TransactionDate = template.FirstOccurrence,
            CategoryId = template.CategoryId,
            Category = category
        }, account, category);
    }

    public static void ValidateSchedule(RecurringTransaction template)
    {
        if (template.FirstOccurrence == default)
        {
            throw new BusinessRuleException("First occurrence is required.");
        }

        if (!Enum.IsDefined(template.Frequency))
        {
            throw new BusinessRuleException("The selected recurring frequency is invalid.");
        }

        if (template.Type == TransactionType.Transfer)
        {
            throw new BusinessRuleException("Recurring transfers are not supported yet.");
        }

        if (!Enum.IsDefined(template.Frequency))
        {
            throw new BusinessRuleException("The selected frequency is invalid.");
        }

        if (template.FirstOccurrence == default)
        {
            throw new BusinessRuleException("First occurrence is required.");
        }

        if (template.LastOccurrence < template.FirstOccurrence)
        {
            throw new BusinessRuleException("Last occurrence cannot be before first occurrence.");
        }
    }
}
