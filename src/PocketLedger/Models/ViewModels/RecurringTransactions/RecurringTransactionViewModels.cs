using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Transactions;

namespace PocketLedger.Models.ViewModels.RecurringTransactions;

public class RecurringTransactionListItemViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public string AccountName { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly FirstOccurrence { get; init; }
    public DateOnly? LastOccurrence { get; init; }
    public RecurringFrequency Frequency { get; init; }
    public bool Enabled { get; init; }
}

public class RecurringTransactionFormViewModel
{
    public Guid Id { get; set; }
    [Required] public TransactionType Type { get; set; } = TransactionType.Expense;
    [Required] public Guid? AccountId { get; set; }
    public Guid? CategoryId { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999999999.9999")] public decimal Amount { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }
    [StringLength(500)] public string? Note { get; set; }
    [Required] public DateOnly FirstOccurrence { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly? LastOccurrence { get; set; }
    [Required] public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;
    public bool Enabled { get; set; } = true;
    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
    public IReadOnlyList<CategoryOptionViewModel> Categories { get; set; } = [];
}
