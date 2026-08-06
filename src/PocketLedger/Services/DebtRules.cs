using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;

namespace PocketLedger.Services;

public static class DebtRules
{
    public static void Validate(Debt debt, Account? account)
    {
        if (string.IsNullOrWhiteSpace(debt.Name)) throw new BusinessRuleException("Debt name is required.");
        if (!CategoryIcons.Exists(debt.Icon)) throw new BusinessRuleException("The selected icon is invalid.");
        if (string.IsNullOrWhiteSpace(debt.CounterpartyName)) throw new BusinessRuleException("Person or bank name is required.");
        if (debt.OriginalAmount <= 0) throw new BusinessRuleException("Original amount must be greater than zero.");
        if (debt.StartDate == default) throw new BusinessRuleException("Start date is required.");
        if (debt.DueDate < debt.StartDate) throw new BusinessRuleException("Due date cannot be before start date.");
        if (!Enum.IsDefined(debt.Direction) || !Enum.IsDefined(debt.Type)) throw new BusinessRuleException("The selected debt direction or type is invalid.");
        if (debt.Direction == DebtDirection.Receivable && debt.Type != DebtType.PrivatePerson) throw new BusinessRuleException("Receivables can currently only belong to a private person.");
        if (account is not null && account.Currency != debt.Currency) throw new BusinessRuleException("Debt and account currencies must match.");
    }

    public static decimal GetDebtDelta(DebtOperationType type, decimal amount) => type switch
    {
        DebtOperationType.Increase or DebtOperationType.ManualCorrectionIncrease or DebtOperationType.LoanDisbursement => amount,
        DebtOperationType.Payment or DebtOperationType.EarlyRepayment or DebtOperationType.ManualCorrectionDecrease or DebtOperationType.ReceivedRepayment => -amount,
        _ => throw new BusinessRuleException("The selected debt operation is invalid.")
    };

    public static bool RequiresAccount(DebtOperationType type) => type is DebtOperationType.Payment or DebtOperationType.EarlyRepayment;
    public static bool AllowsAccount(DebtOperationType type) => RequiresAccount(type) || type is DebtOperationType.LoanDisbursement or DebtOperationType.ReceivedRepayment;
}
