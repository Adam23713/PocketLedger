using PocketLedger.Models.Enums;

namespace PocketLedger.Models.ViewModels.Home;

public class HomeViewModel
{
    public IReadOnlyList<CurrencyBalanceViewModel> MainBalances { get; init; } = [];
    public IReadOnlyList<CurrencyBalanceViewModel> NetWorthBalances { get; init; } = [];
    public IReadOnlyList<CurrencyPeriodViewModel> MonthlyTotals { get; init; } = [];
    public decimal NetWorth { get; init; }
    public int AccountCount { get; init; }
    public decimal IncomeThisMonth { get; init; }
    public decimal ExpensesThisMonth { get; init; }
    public decimal BalanceChangeThisMonth { get; init; }
    public IReadOnlyList<AccountCardViewModel> Accounts { get; init; } = [];
    public IReadOnlyList<RecentTransactionViewModel> RecentTransactions { get; init; } = [];
    public IReadOnlyList<DebtFundingWarningViewModel> DebtFundingWarnings { get; init; } = [];
}

public record CurrencyBalanceViewModel(string Currency, decimal Amount);
public record CurrencyPeriodViewModel(string Currency, decimal Income, decimal Expenses, decimal Change);

public class AccountCardViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public AccountType Type { get; init; }
    public decimal CurrentBalance { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public string IconAlt { get; init; } = string.Empty;
    public string Color { get; init; } = "#ffffff";
}

public class RecentTransactionViewModel
{
    public Guid Id { get; init; }
    public TransactionType Type { get; init; }
    public AdjustmentDirection? AdjustmentDirection { get; init; }
    public string? AccountName { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public string? CategoryIconPath { get; init; }
    public string? CategoryIconAlt { get; init; }
    public decimal Amount { get; init; }
    public DateOnly TransactionDate { get; init; }
    public DebtOperationType? DebtOperationType { get; init; }
    public string? DebtIconPath { get; init; }
    public string? DebtIconAlt { get; init; }
}

public class DebtFundingWarningViewModel
{
    public Guid DebtId { get; init; }
    public string DebtName { get; init; } = string.Empty;
    public DateOnly Date { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    public decimal AccountBalance { get; init; }
    public decimal Shortfall { get; init; }
    public string IconPath { get; init; } = string.Empty;
    public string IconAlt { get; init; } = string.Empty;
}
