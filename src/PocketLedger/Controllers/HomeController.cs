using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PocketLedger.Models.Enums;
using PocketLedger.Models;
using PocketLedger.Models.ViewModels.Home;
using PocketLedger.Models.ViewModels;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Controllers;

public class HomeController(IAccountService accountService, ITransactionService transactionService) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var balances = await accountService.GetCurrentBalancesAsync(cancellationToken);
        var recentTransactions = await transactionService.GetRecentAsync(10, cancellationToken);
        var today = DateTime.Today;
        var monthTransactions = await transactionService.GetForMonthAsync(today.Year, today.Month, cancellationToken);
        var incomeThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Income).Sum(transaction => transaction.Amount);
        var expensesThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Expense).Sum(transaction => transaction.Amount);
        var adjustmentsThisMonth = monthTransactions.Where(transaction => transaction.Type == TransactionType.Adjustment).Sum(transaction => transaction.AdjustmentDirection == AdjustmentDirection.Increase ? transaction.Amount : -transaction.Amount);
        var model = new HomeViewModel
        {
            TotalMainBalance = await transactionService.CalculateMainBalanceAsync(cancellationToken),
            NetWorth = accounts.Where(account => account.IncludeInNetWorth).Sum(account => balances[account.Id]),
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
                var isTransfer = transaction.Type == TransactionType.Transfer;
                return new RecentTransactionViewModel
                {
                    Id = transaction.Id,
                    Type = transaction.Type,
                    AdjustmentDirection = transaction.AdjustmentDirection,
                    AccountName = transaction.Account.Name,
                    Currency = transaction.Account.Currency,
                    CategoryName = isTransfer ? TransactionIcons.TransferDisplayName : transaction.Category?.Name,
                    CategoryIconPath = isTransfer ? TransactionIcons.TransferWebPath : categoryIcon?.WebPath,
                    CategoryIconAlt = isTransfer ? TransactionIcons.TransferDisplayName : categoryIcon?.DisplayName,
                    Amount = transaction.Amount,
                    TransactionDate = transaction.TransactionDate
                };
            }).ToList()
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
