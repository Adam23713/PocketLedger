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
}

public class AccountFormViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public AccountType Type { get; set; }

    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Currency must contain exactly three letters.")]
    public string Currency { get; set; } = "HUF";

    [Display(Name = "Initial balance")]
    public decimal InitialBalance { get; set; }
    public decimal OriginalInitialBalance { get; set; }
    public bool CreateInitialBalanceAdjustment { get; set; }

    [Required]
    public string Icon { get; set; } = AccountIcons.DefaultFor(AccountType.Cash).Id;

    public IReadOnlyList<AccountIconDefinition> AvailableIcons { get; init; } = AccountIcons.All;

    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Color must be a valid hexadecimal color.")]
    public string Color { get; set; } = "#ffffff";

    [Display(Name = "Display order"), Range(0, int.MaxValue)]
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
