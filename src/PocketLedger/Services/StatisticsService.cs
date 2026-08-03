using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class StatisticsService(PocketLedgerDbContext dbContext, IAccountService accountService) : IStatisticsService
{
    public async Task<StatisticsSummary> GetSummaryAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (year < 1 || month is < 1 or > 12) throw new BusinessRuleException("The selected month is invalid.");
        var selectedStart = new DateOnly(year, month, 1);
        var trendStart = selectedStart.AddMonths(-11);
        var end = selectedStart.AddMonths(1);
        var transactions = await dbContext.Transactions.AsNoTracking()
            .Where(transaction => transaction.TransactionDate >= trendStart && transaction.TransactionDate < end && transaction.Type != TransactionType.Transfer)
            .Select(transaction => new StatisticsTransactionRow(
                transaction.TransactionDate,
                transaction.Type,
                transaction.Amount,
                transaction.AdjustmentDirection,
                transaction.CategoryId,
                transaction.Category != null ? transaction.Category.Name : null,
                transaction.Category != null ? transaction.Category.Icon : null,
                transaction.Category != null && transaction.Category.ParentCategory != null ? transaction.Category.ParentCategory.Icon : null))
            .ToListAsync(cancellationToken);
        var selected = transactions.Where(transaction => transaction.TransactionDate >= selectedStart).ToList();
        var selectedTotals = PeriodCalculator.Calculate(selected.Select(ToTransaction));
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var balances = await accountService.GetCurrentBalancesAsync(cancellationToken);

        var trend = Enumerable.Range(0, 12).Select(offset =>
        {
            var start = trendStart.AddMonths(offset);
            var next = start.AddMonths(1);
            var monthItems = transactions.Where(item => item.TransactionDate >= start && item.TransactionDate < next).ToList();
            var totals = PeriodCalculator.Calculate(monthItems.Select(ToTransaction));
            return new StatisticsMonthlyTrend(start.Year, start.Month, totals.Income, totals.Expenses, totals.Savings, totals.Balance);
        }).ToList();

        return new StatisticsSummary(
            selectedTotals.Income,
            selectedTotals.Expenses,
            selectedTotals.Savings,
            selectedTotals.Balance,
            CategoryTotals(selected, TransactionType.Income),
            CategoryTotals(selected, TransactionType.Expense),
            accounts.Select(account => new StatisticsAccountBalance(account.Id, account.Name, account.Currency, balances[account.Id])).ToList(),
            trend);
    }

    private static IReadOnlyList<StatisticsCategoryTotal> CategoryTotals(IEnumerable<StatisticsTransactionRow> transactions, TransactionType type)
    {
        return transactions.Where(item => item.Type == type)
            .GroupBy(item => new { item.CategoryId, item.CategoryName, Icon = item.ParentCategoryIcon ?? item.CategoryIcon })
            .Select(group => new StatisticsCategoryTotal(group.Key.CategoryId, group.Key.CategoryName ?? "Uncategorized", group.Sum(item => item.Amount), group.Key.Icon, type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense))
            .OrderByDescending(item => item.Amount)
            .ToList();
    }

    private record StatisticsTransactionRow(DateOnly TransactionDate, TransactionType Type, decimal Amount, AdjustmentDirection? AdjustmentDirection, Guid? CategoryId, string? CategoryName, string? CategoryIcon, string? ParentCategoryIcon);

    private static Models.Entities.Transaction ToTransaction(StatisticsTransactionRow row) => new() { TransactionDate = row.TransactionDate, Type = row.Type, Amount = row.Amount, AdjustmentDirection = row.AdjustmentDirection };
}
