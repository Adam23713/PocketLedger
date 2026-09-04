using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Transactions;

namespace PocketLedger.Models.ViewModels.RecurringTransactions;

public class RecurringTransactionIndexViewModel
{
    public IReadOnlyList<RecurringTransactionListItemViewModel> Items { get; init; } = [];
    public IReadOnlyList<RecurringTransactionListItemViewModel> LoanItems { get; init; } = [];
    public IReadOnlyList<RecurringTransactionExpenseTotalViewModel> ExpenseTotals { get; init; } = [];
    public IReadOnlyList<RecurringTransactionExpenseTotalViewModel> MonthlyLoanPaymentTotals { get; init; } = [];
}

public record RecurringTransactionExpenseTotalViewModel(string Currency, decimal Amount);

public class RecurringTransactionListItemViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public AdjustmentDirection? AdjustmentDirection { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public string? Note { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly FirstOccurrence { get; init; }
    public DateOnly? LastOccurrence { get; init; }
    public DateOnly? NextOccurrence { get; init; }
    public RecurringFrequency Frequency { get; init; }
    public bool Enabled { get; init; }
    public Guid? DebtId { get; init; }
    public string? DebtName { get; init; }
}

public class RecurringTransactionFormViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    [Required] public TransactionType Type { get; set; } = TransactionType.Expense;
    [Required] public Guid? AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999999999.9999")] public decimal Amount { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    [Required] public DateOnly FirstOccurrence { get; set; }
    public DateOnly? LastOccurrence { get; set; }
    public bool NoEndDate { get; set; } = true;
    [Required] public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
    public IReadOnlyList<CategoryOptionViewModel> Categories { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!NoEndDate && LastOccurrence is null) yield return new ValidationResult("Last occurrence is required when an end date is selected.", [nameof(LastOccurrence)]);
    }
}
