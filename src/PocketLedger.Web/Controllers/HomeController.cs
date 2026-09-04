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
        var classifiedMonth = monthTransactions.Select(transaction => (Transaction: transaction, Semantics: TransactionSemantics.Resolve(transaction.Type, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.DebtOperationType))).ToList();
        // Dashboard headline income/expense intentionally excludes adjustments; BalanceChange includes their account effect.
        var incomeThisMonth = classifiedMonth.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Income).Sum(item => item.Transaction.Amount);
        var expensesThisMonth = classifiedMonth.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Expense).Sum(item => item.Transaction.Amount);
        var adjustmentsThisMonth = classifiedMonth.Where(item => item.Semantics.ReportingClassification is TransactionReportingClassification.AdjustmentIncrease or TransactionReportingClassification.AdjustmentDecrease).Sum(item => item.Semantics.SourceAccountChange);
        var monthlyTotals = classifiedMonth.Where(item => item.Semantics.ReportingClassification != TransactionReportingClassification.Excluded).GroupBy(item => item.Transaction.SourceCurrency).Select(group => new CurrencyPeriodViewModel(group.Key, group.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Income).Sum(item => item.Transaction.Amount), group.Where(item => item.Semantics.ReportingClassification == TransactionReportingClassification.Expense).Sum(item => item.Transaction.Amount), group.Sum(item => item.Semantics.SourceAccountChange))).ToList();
        var warnings = await debtService.GetFundingWarningsAsync(today, cancellationToken);
        var model = new HomeViewModel
        {
            MainBalances = (await transactionService.CalculateMainBalanceAsync(cancellationToken)).Select(balance => new CurrencyBalanceViewModel(balance.Currency, balance.Amount)).ToList(),
            NetWorth = accounts.Where(account => account.IncludeInNetWorth).Sum(account => balances[account.Id]),
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
                var categoryIcon = TransactionIcons.Resolve(transaction);
                var debtIcon = transaction.Debt is null ? null : CategoryIcons.Resolve(transaction.Debt.Icon);
                return new RecentTransactionViewModel
                {
                    Id = transaction.Id,
                    Type = transaction.Type,
                    AdjustmentDirection = transaction.AdjustmentDirection,
                    AccountName = transaction.Account?.Name,
                    Currency = transaction.Account?.Currency ?? transaction.Debt?.Currency ?? string.Empty,
                    CategoryName = transaction.Category?.Name ?? categoryIcon?.DisplayName,
                    CategoryIconPath = categoryIcon?.WebPath,
                    CategoryIconAlt = categoryIcon?.DisplayName,
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
