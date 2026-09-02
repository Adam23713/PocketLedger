using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class DebtBalanceCalculatorTests
{
    [Theory]
    [InlineData(DebtOperationType.Increase, 1200)]
    [InlineData(DebtOperationType.ManualCorrectionIncrease, 1200)]
    [InlineData(DebtOperationType.LoanDisbursement, 1200)]
    [InlineData(DebtOperationType.Payment, 800)]
    [InlineData(DebtOperationType.EarlyRepayment, 800)]
    [InlineData(DebtOperationType.ManualCorrectionDecrease, 800)]
    [InlineData(DebtOperationType.ReceivedRepayment, 800)]
    public void Calculate_AppliesOperationTypeToOriginalAmount(DebtOperationType operationType, decimal expected)
    {
        var operation = CreateOperation(operationType, 200);

        var remaining = DebtBalanceCalculator.Calculate(1000, [operation]);

        Assert.Equal(expected, remaining);
    }

    [Fact]
    public void Calculate_AppliesPayableOperationsInSequence()
    {
        var operations = new[]
        {
            CreateOperation(DebtOperationType.Payment, 200),
            CreateOperation(DebtOperationType.Increase, 50),
            CreateOperation(DebtOperationType.EarlyRepayment, 100),
            CreateOperation(DebtOperationType.ManualCorrectionIncrease, 25),
            CreateOperation(DebtOperationType.ManualCorrectionDecrease, 10)
        };

        Assert.Equal(765, DebtBalanceCalculator.Calculate(1000, operations));
    }

    [Fact]
    public void Calculate_AppliesReceivableOperationsInSequence()
    {
        var operations = new[]
        {
            CreateOperation(DebtOperationType.LoanDisbursement, 200),
            CreateOperation(DebtOperationType.ReceivedRepayment, 150),
            CreateOperation(DebtOperationType.ManualCorrectionDecrease, 25)
        };

        Assert.Equal(1025, DebtBalanceCalculator.Calculate(1000, operations));
    }

    [Fact]
    public void Calculate_IgnoresNonDebtTransactionsAndExcludedOperation()
    {
        var excluded = CreateOperation(DebtOperationType.Payment, 300);
        var nonDebtTransaction = new Transaction { Id = Guid.NewGuid(), Amount = 500, DebtOperationType = null };

        var remaining = DebtBalanceCalculator.Calculate(1000, [excluded, nonDebtTransaction], excluded.Id);

        Assert.Equal(1000, remaining);
    }

    [Fact]
    public void Calculate_ReturnsOriginalAmountWhenThereAreNoOperations()
    {
        Assert.Equal(1000, DebtBalanceCalculator.Calculate(1000, []));
    }

    private static Transaction CreateOperation(DebtOperationType type, decimal amount) => new() { Id = Guid.NewGuid(), DebtOperationType = type, Amount = amount };
}
