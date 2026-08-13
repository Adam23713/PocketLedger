using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class PocketLedgerFeatureTests
{
    [Theory]
    [InlineData(100000, 20000, "2026-08-31", RecurringFrequency.Monthly, "2026-12-31")]
    [InlineData(100001, 20000, "2026-08-31", RecurringFrequency.Monthly, "2027-01-31")]
    [InlineData(300, 100, "2026-08-14", RecurringFrequency.Weekly, "2026-08-28")]
    public void LastPaymentDate_UsesRemainingAmountAndSchedule(decimal remaining, decimal payment, string first, RecurringFrequency frequency, string expected)
    {
        var result = DebtRules.CalculateLastPaymentDate(remaining, payment, DateOnly.Parse(first), frequency);

        Assert.Equal(DateOnly.Parse(expected), result);
    }

    [Fact]
    public void AutomaticPayment_CapsFinalInstallmentAtRemainingAmount()
    {
        Assert.Equal(545m, DebtRules.GetAutomaticPaymentAmount(45477m, 545m));
        Assert.Equal(45477m, DebtRules.GetAutomaticPaymentAmount(45477m, 100000m));
    }

    [Theory]
    [InlineData(1000, 650, 35)]
    [InlineData(1000, 1200, 0)]
    [InlineData(1000, -50, 100)]
    public void LoanProgress_IsAmountBasedAndClamped(decimal original, decimal remaining, decimal expected)
    {
        Assert.Equal(expected, DebtRules.CalculateProgressPercentage(original, remaining));
    }

    [Theory]
    [InlineData(1200, RecurringFrequency.Yearly, 100)]
    [InlineData(100, RecurringFrequency.Monthly, 100)]
    [InlineData(120, RecurringFrequency.Weekly, 520)]
    [InlineData(12, RecurringFrequency.Daily, 365)]
    public void MonthlyLoanTotal_NormalizesFrequency(decimal amount, RecurringFrequency frequency, decimal expected)
    {
        Assert.Equal(expected, RecurringSchedule.ToMonthlyAmount(amount, frequency));
    }

    public static TheoryData<decimal, decimal?, bool> TransactionAmounts => new()
    {
        { 100.5m, null, false },
        { 100m, 20.5m, false },
        { 100m, 20m, true }
    };

    [Theory, MemberData(nameof(TransactionAmounts))]
    public void TransactionAmount_RequiresWholeNumbers(decimal amount, decimal? targetAmount, bool expectedValid)
    {
        var model = new TransactionFormViewModel { AccountId = Guid.NewGuid(), Amount = amount, TargetAmount = targetAmount };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(model, new ValidationContext(model), results, true);

        Assert.Equal(expectedValid, valid);
    }
}
