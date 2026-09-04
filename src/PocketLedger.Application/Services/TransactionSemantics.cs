using PocketLedger.Models.Enums;

namespace PocketLedger.Services;

public enum TransactionReportingClassification
{
    Income,
    Expense,
    AdjustmentIncrease,
    AdjustmentDecrease,
    Excluded
}

public readonly record struct TransactionFinancialSemantics(decimal SourceAccountChange, decimal TargetAccountChange, TransactionReportingClassification ReportingClassification);

public static class TransactionSemantics
{
    public static TransactionFinancialSemantics Resolve(TransactionType type, decimal amount, decimal? targetAmount = null, AdjustmentDirection? adjustmentDirection = null, DebtOperationType? debtOperationType = null)
    {
        if (!Enum.IsDefined(type)) throw new BusinessRuleException("The transaction type is invalid.");
        if (debtOperationType is not null && !Enum.IsDefined(debtOperationType.Value)) throw new BusinessRuleException("The debt operation type is invalid.");
        if (type != TransactionType.Transfer && targetAmount is not null) throw new BusinessRuleException("Only transfers can define a target amount.");
        if (type != TransactionType.Adjustment && adjustmentDirection is not null) throw new BusinessRuleException("Only adjustments can define an adjustment direction.");

        return type switch
        {
            TransactionType.Income => ResolveIncome(amount, debtOperationType),
            TransactionType.Expense => ResolveExpense(amount, debtOperationType),
            TransactionType.Transfer when debtOperationType is null && adjustmentDirection is null => new(-amount, targetAmount ?? amount, TransactionReportingClassification.Excluded),
            TransactionType.Transfer => throw new BusinessRuleException("The transfer financial semantics are invalid."),
            TransactionType.Adjustment when debtOperationType is null && adjustmentDirection == AdjustmentDirection.Increase => new(amount, 0, TransactionReportingClassification.AdjustmentIncrease),
            TransactionType.Adjustment when debtOperationType is null && adjustmentDirection == AdjustmentDirection.Decrease => new(-amount, 0, TransactionReportingClassification.AdjustmentDecrease),
            TransactionType.Adjustment => throw new BusinessRuleException("The adjustment financial semantics are invalid."),
            TransactionType.DebtEntry => ResolveDebtEntry(debtOperationType),
            _ => throw new BusinessRuleException("The transaction financial semantics are unsupported.")
        };
    }

    public static TransactionType GetDebtTransactionType(DebtOperationType operationType, bool hasAccount)
    {
        if (!Enum.IsDefined(operationType)) throw new BusinessRuleException("The debt operation type is invalid.");
        if (!hasAccount) return TransactionType.DebtEntry;
        return operationType == DebtOperationType.ReceivedRepayment ? TransactionType.Income : TransactionType.Expense;
    }

    private static TransactionFinancialSemantics ResolveIncome(decimal amount, DebtOperationType? debtOperationType)
    {
        if (debtOperationType is not (null or DebtOperationType.ReceivedRepayment)) throw new BusinessRuleException("The income debt operation is invalid.");
        return new(amount, 0, TransactionReportingClassification.Income);
    }

    private static TransactionFinancialSemantics ResolveExpense(decimal amount, DebtOperationType? debtOperationType)
    {
        if (debtOperationType is not (null or DebtOperationType.Payment or DebtOperationType.EarlyRepayment or DebtOperationType.LoanDisbursement)) throw new BusinessRuleException("The expense debt operation is invalid.");
        return new(-amount, 0, TransactionReportingClassification.Expense);
    }

    private static TransactionFinancialSemantics ResolveDebtEntry(DebtOperationType? debtOperationType)
    {
        if (debtOperationType is not (DebtOperationType.Increase or DebtOperationType.ManualCorrectionIncrease or DebtOperationType.ManualCorrectionDecrease or DebtOperationType.LoanDisbursement or DebtOperationType.ReceivedRepayment))
            throw new BusinessRuleException("The debt entry operation is invalid.");
        return new(0, 0, TransactionReportingClassification.Excluded);
    }
}
