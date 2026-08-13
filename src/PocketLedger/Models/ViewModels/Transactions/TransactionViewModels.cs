using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using PocketLedger.Models.Enums;

namespace PocketLedger.Models.ViewModels.Transactions;

public class TransactionIndexViewModel
{
    public int Year { get; init; }
    public int Month { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? CategoryId { get; init; }
    public TransactionType? Type { get; init; }
    public decimal? AmountFrom { get; init; }
    public decimal? AmountTo { get; init; }
    public string? Search { get; init; }
    public int Page { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<SelectListItem> Accounts { get; init; } = [];
    public IReadOnlyList<SelectListItem> Categories { get; init; } = [];
    public IReadOnlyList<TransactionDayGroupViewModel> DayGroups { get; init; } = [];
}

public class TransactionDayGroupViewModel
{
    public DateOnly Date { get; init; }
    public IReadOnlyList<TransactionDailyTotalViewModel> Totals { get; init; } = [];
    public IReadOnlyList<TransactionListItemViewModel> Transactions { get; init; } = [];
}

public class TransactionDailyTotalViewModel
{
    public string Currency { get; init; } = string.Empty;
    public decimal Income { get; init; }
    public decimal Expenses { get; init; }
}

public class TransactionListItemViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public AdjustmentDirection? AdjustmentDirection { get; init; }
    public string? AccountName { get; init; }
    public string? TargetAccountName { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? TargetCurrency { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public decimal Amount { get; init; }
    public decimal? TargetAmount { get; init; }
    public TimeOnly TransactionTime { get; init; }
    public string? Note { get; init; }
    public string? DebtName { get; init; }
    public DebtOperationType? DebtOperationType { get; init; }
    public string? DebtIconPath { get; init; }
    public string? DebtIconAlt { get; init; }
}

public class TransactionDetailsViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public AdjustmentDirection? AdjustmentDirection { get; init; }
    public string? AccountName { get; init; }
    public string? TargetAccountName { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? TargetCurrency { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public decimal Amount { get; init; }
    public decimal? TargetAmount { get; init; }
    public DateOnly TransactionDate { get; init; }
    public TimeOnly TransactionTime { get; init; }
    public string? Note { get; init; }
    public Guid? DebtId { get; init; }
    public string? DebtName { get; init; }
    public DebtOperationType? DebtOperationType { get; init; }
    public string? DebtIconPath { get; init; }
    public string? DebtIconAlt { get; init; }
}

public class TransactionFormViewModel : IValidatableObject
{
    public TransactionFormViewModel()
    {
        var now = DateTime.Now;
        TransactionHour = now.Hour;
        TransactionMinute = now.Minute;
    }

    public Guid Id { get; set; }

    [Required]
    public TransactionType Type { get; set; } = TransactionType.Expense;

    [Required(ErrorMessage = "Account is required.")]
    public Guid? AccountId { get; set; }

    public Guid? TargetAccountId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999999999.9999", ErrorMessage = "Amount must be greater than zero.")]
    public decimal Amount { get; set; }

    public decimal? TargetAmount { get; set; }

    [Required]
    public DateOnly TransactionDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [Range(0, 23)]
    public int TransactionHour { get; set; }

    [Range(0, 59)]
    public int TransactionMinute { get; set; }

    public Guid? CategoryId { get; set; }
    public AdjustmentDirection? AdjustmentDirection { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
    public IReadOnlyList<CategoryOptionViewModel> Categories { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount != decimal.Truncate(Amount)) yield return new ValidationResult("Amount must be a whole number.", [nameof(Amount)]);
        if (TargetAmount is not null && TargetAmount != decimal.Truncate(TargetAmount.Value)) yield return new ValidationResult("Target amount must be a whole number.", [nameof(TargetAmount)]);
    }
}

public class CategoryOptionViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CategoryType Type { get; init; }
    public bool IsSubcategory { get; init; }
    public string IconPath { get; init; } = string.Empty;
    public string IconAlt { get; init; } = string.Empty;
}

public class AccountOptionViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
}

public class TransactionDeleteViewModel : TransactionDetailsViewModel;
