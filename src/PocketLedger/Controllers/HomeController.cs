using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Home;
using PocketLedger.Models.ViewModels;
using PocketLedger.Services.Interfaces;
using PocketLedger.Services;

namespace PocketLedger.Controllers;

public class HomeController(IAccountService accountService, ITransactionService transactionService, IDebtService debtService, IUserContextService userContext) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var balances = await accountService.GetCurrentBalancesAsync(cancellationToken);
        var recentTransactions = await transactionService.GetRecentAsync(10, cancellationToken);
        var today = await userContext.TodayAsync(cancellationToken);
        var monthTransactions = await transactionService.GetForMonthAsync(today.Year, today.Month, cancellationToken);
        var incomeThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Income).Sum(transaction => transaction.Amount);
        var expensesThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Expense).Sum(transaction => transaction.Amount);
        var adjustmentsThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Adjustment).Sum(transaction => transaction.AdjustmentDirection == AdjustmentDirection.Increase ? transaction.Amount : -transaction.Amount);
        var monthlyTotals = monthTransactions.Where(transaction => transaction.Type != TransactionType.Transfer).GroupBy(transaction => transaction.SourceCurrency).Select(group => new CurrencyPeriodViewModel(group.Key, group.Where(item => item.Type == TransactionType.Income).Sum(item => item.Amount), group.Where(item => item.Type == TransactionType.Expense).Sum(item => item.Amount), group.Sum(item => item.Type == TransactionType.Income ? item.Amount : item.Type == TransactionType.Expense ? -item.Amount : item.AdjustmentDirection == AdjustmentDirection.Increase ? item.Amount : -item.Amount))).ToList();
        var warnings = await debtService.GetFundingWarningsAsync(today, cancellationToken);
        var model = new HomeViewModel
        {
            TotalMainBalance = await transactionService.CalculateMainBalanceAsync(cancellationToken),
            NetWorth = accounts.Where(account => account.IncludeInNetWorth).Sum(account => balances[account.Id]),
            MainBalances = accounts.Where(account => account.IncludeInMainBalance).GroupBy(account => account.Currency).Select(group => new CurrencyBalanceViewModel(group.Key, group.Sum(account => balances[account.Id]))).ToList(),
            NetWorthBalances = accounts.Where(account => account.IncludeInNetWorth).GroupBy(account => account.Currency).Select(group => new CurrencyBalanceViewModel(group.Key, group.Sum(account => balances[account.Id]))).ToList(),
            MonthlyTotals = monthlyTotals,
            AccountCount = accounts.Count,
            IncomeThisMonth = incomeThisMonth,
            ExpensesThisMonth = expensesThisMonth,
            BalanceChangeThisMonth = incomeThisMonth - expensesThisMonth + adjustmentsThisMonth,
            Accounts = accounts.Select(account =>
            {
                var icon = AccountIcons.Resolve(account.Icon, account.Type);
                return new AccountCardViewModel
                {
                    Id = account.Id,
                    Name = account.Name,
                    Type = account.Type,
                    CurrentBalance = balances[account.Id],
                    Currency = account.Currency,
                    IconPath = icon.WebPath,
                    IconAlt = icon.DisplayName,
                    Color = account.Color
                };
            }).ToList(),
            RecentTransactions = recentTransactions.Select(transaction =>
            {
                var categoryIcon = transaction.Category is null ? null : CategoryIcons.Resolve(transaction.Category);
                var debtIcon = transaction.Debt is null ? null : CategoryIcons.Resolve(transaction.Debt.Icon);
                var isTransfer = transaction.Type == TransactionType.Transfer;
                return new RecentTransactionViewModel
                {
                    Id = transaction.Id,
                    Type = transaction.Type,
                    AdjustmentDirection = transaction.AdjustmentDirection,
                    AccountName = transaction.Account?.Name,
                    Currency = transaction.Account?.Currency ?? transaction.Debt?.Currency ?? string.Empty,
                    CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
                    CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : categoryIcon?.WebPath,
                    CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : categoryIcon?.DisplayName,
                    Amount = transaction.Amount,
                    TransactionDate = transaction.TransactionDate,
                    DebtOperationType = transaction.DebtOperationType,
                    DebtIconPath = debtIcon?.WebPath,
                    DebtIconAlt = debtIcon?.DisplayName
                };
            }).ToList(),
            DebtFundingWarnings = warnings.Select(item => { var icon = CategoryIcons.Resolve(item.DebtIcon); return new DebtFundingWarningViewModel { DebtId = item.DebtId, DebtName = item.DebtName, IconPath = icon.WebPath, IconAlt = icon.DisplayName, Date = item.Date, Amount = item.Amount, Currency = item.Currency, AccountName = item.AccountName, AccountBalance = item.AccountBalance, Shortfall = item.Shortfall }; }).ToList()
        };
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
