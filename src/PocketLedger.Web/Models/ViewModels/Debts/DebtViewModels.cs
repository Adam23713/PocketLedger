using PocketLedger.Localization;
using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;
using PocketLedger.Models.ViewModels.Transactions;
using PocketLedger.Models;
using PocketLedger.Services;

namespace PocketLedger.Models.ViewModels.Debts;

public class DebtIndexViewModel
{
    public IReadOnlyList<DebtCurrencyGroupViewModel> ActiveGroups { get; init; } = [];
    public IReadOnlyList<DebtCurrencyGroupViewModel> ClosedGroups { get; init; } = [];
}

public record DebtCurrencyGroupViewModel(string Currency, IReadOnlyList<DebtListItemViewModel> Items, decimal WeOwe, decimal OwedToUs);

public class DebtListItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string IconAlt { get; init; } = string.Empty;
    public DebtDirection Direction { get; init; }
    public DebtType Type { get; init; }
    public decimal OriginalAmount { get; init; }
    public decimal RemainingAmount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public DateOnly? NextPayment { get; init; }
    public string? AccountName { get; init; }
    public DebtStatus Status { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal ProgressPercentage { get; init; }
}

public class DebtFormViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "The {0} field is required."), StringLength(200, ErrorMessage = "The field {0} must be a string with a maximum length of {1}.")] public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "The {0} field is required."), StringLength(100, ErrorMessage = "The field {0} must be a string with a maximum length of {1}.")] public string Icon { get; set; } = string.Empty;
    public IReadOnlyList<CategoryIconDefinition> AvailableIcons { get; init; } = CategoryIcons.All;
    [Required(ErrorMessage = "The {0} field is required.")] public DebtDirection Direction { get; set; }
    [Required(ErrorMessage = "The {0} field is required.")] public DebtType Type { get; set; }
    [Required(ErrorMessage = "The {0} field is required."), StringLength(200, ErrorMessage = "The field {0} must be a string with a maximum length of {1}."), Display(Name = "Person or bank name")] public string CounterpartyName { get; set; } = string.Empty;
    [Display(Name = "Original amount"), Range(typeof(decimal), "0.0001", "999999999999999.9999", ErrorMessage = "The field {0} must be between {1} and {2}.")] public decimal OriginalAmount { get; set; }
    [Required(ErrorMessage = "The {0} field is required."), StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = "EUR";
    [Required(ErrorMessage = "The {0} field is required.")] public DateOnly StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    [StringLength(500, ErrorMessage = "The field {0} must be a string with a maximum length of {1}.")] public string? Note { get; set; }
    public Guid? AccountId { get; set; }
    public bool AutomaticPaymentEnabled { get; set; }
    [Display(Name = "Automatic payment amount")]
    public decimal? AutomaticPaymentAmount { get; set; }
    [Display(Name = "Next payment date")]
    public DateOnly? NextPaymentDate { get; set; }
    [Display(Name = "Last payment date")]
    public DateOnly? LastPaymentDate { get; set; }
    public RecurringFrequency Frequency { get; set; } = RecurringFrequency.Monthly;
    public decimal ExistingOriginalAmount { get; set; }
    public decimal RemainingAmountForSchedule { get; set; }
    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AutomaticPaymentEnabled && AccountId is null) yield return new ValidationResult(CommonText.Get("An account is required for automatic payment."), [nameof(AccountId)]);
        if (AutomaticPaymentEnabled && AutomaticPaymentAmount is not > 0) yield return new ValidationResult(CommonText.Get("A positive automatic payment amount is required."), [nameof(AutomaticPaymentAmount)]);
        if (AutomaticPaymentEnabled && NextPaymentDate is null) yield return new ValidationResult(CommonText.Get("Next payment date is required."), [nameof(NextPaymentDate)]);
    }
}

public class DebtDetailsViewModel
{
    public DebtListItemViewModel Summary { get; init; } = new();
    public string CounterpartyName { get; init; } = string.Empty;
    public DateOnly StartDate { get; init; }
    public DateOnly? DueDate { get; init; }
    public string? Note { get; init; }
    public decimal? AutomaticPaymentAmount { get; init; }
    public RecurringFrequency? Frequency { get; init; }
    public IReadOnlyList<DebtOperationListItemViewModel> Operations { get; init; } = [];
}

public class DebtDeleteViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int TransactionCount { get; init; }
    public int RecurringTransactionCount { get; init; }
    public int AffectedAccountCount { get; init; }
}

public class DebtOperationListItemViewModel
{
    public Guid Id { get; init; }
    public DebtOperationType Type { get; init; }
    public decimal Amount { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly Time { get; init; }
    public string? AccountName { get; init; }
    public string? Note { get; init; }
}

public class DebtOperationFormViewModel
{
    public Guid TransactionId { get; set; }
    public Guid DebtId { get; set; }
    public DebtDirection Direction { get; set; }
    [Required(ErrorMessage = "The {0} field is required.")] public DebtOperationType Type { get; set; }
    [Range(typeof(decimal), "0.0001", "999999999999999.9999", ErrorMessage = "The field {0} must be between {1} and {2}.")] public decimal Amount { get; set; }
    public Guid? AccountId { get; set; }
    [Required(ErrorMessage = "The {0} field is required.")] public DateOnly Date { get; set; }
    [StringLength(500, ErrorMessage = "The field {0} must be a string with a maximum length of {1}.")] public string? Note { get; set; }
    public DebtOverpaymentResolution? OverpaymentResolution { get; set; }
    public bool RequiresOverpaymentResolution { get; set; }
    public decimal OverpaymentDifference { get; set; }
    public string Currency { get; set; } = string.Empty;
    public IReadOnlyList<AccountOptionViewModel> Accounts { get; set; } = [];
}
