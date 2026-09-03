using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class TransactionSemanticsTests
{
    [Theory]
    [InlineData(TransactionType.Income, 100, null, 100, 0, TransactionReportingClassification.Income)]
    [InlineData(TransactionType.Expense, 100, null, -100, 0, TransactionReportingClassification.Expense)]
    [InlineData(TransactionType.Adjustment, 100, AdjustmentDirection.Increase, 100, 0, TransactionReportingClassification.AdjustmentIncrease)]
    [InlineData(TransactionType.Adjustment, 100, AdjustmentDirection.Decrease, -100, 0, TransactionReportingClassification.AdjustmentDecrease)]
    public void Resolve_DefinesSourceAccountImpactAndReporting(TransactionType type, decimal amount, AdjustmentDirection? direction, decimal sourceChange, decimal targetChange, TransactionReportingClassification classification)
    {
        var result = TransactionSemantics.Resolve(type, amount, adjustmentDirection: direction);

        Assert.Equal(sourceChange, result.SourceAccountChange);
        Assert.Equal(targetChange, result.TargetAccountChange);
        Assert.Equal(classification, result.ReportingClassification);
    }

    [Fact]
    public void Resolve_DefinesBothTransferAccountImpacts()
    {
        var result = TransactionSemantics.Resolve(TransactionType.Transfer, 100, 125);

        Assert.Equal(-100, result.SourceAccountChange);
        Assert.Equal(125, result.TargetAccountChange);
        Assert.Equal(TransactionReportingClassification.Excluded, result.ReportingClassification);
    }

    [Theory]
    [InlineData(DebtOperationType.Payment, true, TransactionType.Expense)]
    [InlineData(DebtOperationType.EarlyRepayment, true, TransactionType.Expense)]
    [InlineData(DebtOperationType.LoanDisbursement, true, TransactionType.Expense)]
    [InlineData(DebtOperationType.ReceivedRepayment, true, TransactionType.Income)]
    [InlineData(DebtOperationType.Increase, false, TransactionType.DebtEntry)]
    [InlineData(DebtOperationType.ManualCorrectionIncrease, false, TransactionType.DebtEntry)]
    [InlineData(DebtOperationType.ManualCorrectionDecrease, false, TransactionType.DebtEntry)]
    [InlineData(DebtOperationType.LoanDisbursement, false, TransactionType.DebtEntry)]
    [InlineData(DebtOperationType.ReceivedRepayment, false, TransactionType.DebtEntry)]
    public void DebtOperations_HaveExplicitTransactionAndFinancialSemantics(DebtOperationType operationType, bool hasAccount, TransactionType expectedType)
    {
        var type = TransactionSemantics.GetDebtTransactionType(operationType, hasAccount);
        var result = TransactionSemantics.Resolve(type, 50, debtOperationType: operationType);

        Assert.Equal(expectedType, type);
        Assert.Equal(hasAccount && expectedType == TransactionType.Income ? 50 : hasAccount ? -50 : 0, result.SourceAccountChange);
        Assert.Equal(hasAccount && expectedType == TransactionType.Income ? TransactionReportingClassification.Income : hasAccount ? TransactionReportingClassification.Expense : TransactionReportingClassification.Excluded, result.ReportingClassification);
    }

    [Theory]
    [InlineData((TransactionType)999, null, null, null)]
    [InlineData(TransactionType.Transfer, null, AdjustmentDirection.Increase, null)]
    [InlineData(TransactionType.Adjustment, null, null, null)]
    [InlineData(TransactionType.Income, null, null, DebtOperationType.Payment)]
    [InlineData(TransactionType.DebtEntry, null, null, DebtOperationType.Payment)]
    public void Resolve_RejectsUnknownOrUnsupportedCombinations(TransactionType type, decimal? targetAmount, AdjustmentDirection? direction, DebtOperationType? debtOperationType)
    {
        Assert.Throws<BusinessRuleException>(() => TransactionSemantics.Resolve(type, 100, targetAmount, direction, debtOperationType));
    }

    [Fact]
    public void Resolve_RejectsUnknownDebtOperation()
    {
        Assert.Throws<BusinessRuleException>(() => TransactionSemantics.Resolve(TransactionType.DebtEntry, 100, debtOperationType: (DebtOperationType)999));
        Assert.Throws<BusinessRuleException>(() => TransactionSemantics.GetDebtTransactionType((DebtOperationType)999, false));
    }
}
