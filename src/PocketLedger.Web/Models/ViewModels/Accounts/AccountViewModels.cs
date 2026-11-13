using System.ComponentModel.DataAnnotations;
using PocketLedger.Models.Enums;

namespace PocketLedger.Models.ViewModels.Accounts;

public class AccountListItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public AccountType Type { get; init; }
    public decimal CurrentBalance { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string IconAlt { get; init; } = string.Empty;
    public string Color { get; init; } = "#ffffff";
    public int DisplayOrder { get; init; }
    public bool IncludeInMainBalance { get; init; }
    public bool IncludeInNetWorth { get; init; }
    public bool IncludeInStatistics { get; init; }
}

public class AccountDetailsViewModel
{
    public AccountListItemViewModel Account { get; init; } = new();
    public decimal InitialBalance { get; init; }
    public IReadOnlyList<AccountTransactionViewModel> RecentTransactions { get; init; } = [];
}

public class AccountTransactionViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public AdjustmentDirection? AdjustmentDirection { get; init; }
    public DateOnly Date { get; init; }
    public decimal Amount { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public string? DebtName { get; init; }
    public DebtOperationType? DebtOperationType { get; init; }
    public string? DebtIconPath { get; init; }
    public string? DebtIconAlt { get; init; }
}

public class AccountFormViewModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "The {0} field is required."), StringLength(100, ErrorMessage = "The field {0} must be a string with a maximum length of {1}.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "The {0} field is required.")]
    public AccountType Type { get; set; }

    [Required(ErrorMessage = "The {0} field is required."), RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must contain exactly three letters.")]
    public string Currency { get; set; } = "EUR";

    [Display(Name = "Initial balance")]
    public decimal InitialBalance { get; set; }
    public decimal OriginalInitialBalance { get; set; }
    public bool CreateInitialBalanceAdjustment { get; set; }

    [Required(ErrorMessage = "The {0} field is required.")]
    public string Icon { get; set; } = AccountIcons.DefaultFor(AccountType.Cash).Id;

    public IReadOnlyList<AccountIconDefinition> AvailableIcons { get; init; } = AccountIcons.All;

    [Required(ErrorMessage = "The {0} field is required."), RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a valid hexadecimal color.")]
    public string Color { get; set; } = "#ffffff";

    [Display(Name = "Display order"), Range(0, int.MaxValue, ErrorMessage = "The field {0} must be between {1} and {2}.")]
    public int DisplayOrder { get; set; }

    [Display(Name = "Include in main balance")]
    public bool IncludeInMainBalance { get; set; } = true;
    [Display(Name = "Include in net worth")]
    public bool IncludeInNetWorth { get; set; } = true;
    [Display(Name = "Include in statistics")]
    public bool IncludeInStatistics { get; set; } = true;
}

public class AccountDeleteViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal CurrentBalance { get; init; }
    public string Currency { get; init; } = string.Empty;
    public int TransactionCount { get; init; }
    public int RecurringTransactionCount { get; init; }
    public int DebtCount { get; init; }
    public bool HasRelatedData => TransactionCount + RecurringTransactionCount + DebtCount > 0;
}
