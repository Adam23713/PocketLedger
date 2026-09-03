using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class StatisticsService(PocketLedgerDbContext dbContext, IAccountService accountService) : IStatisticsService
{
    public async Task<IReadOnlyList<string>> GetAvailableCurrenciesAsync(int year, int month, CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var transactionRows = await dbContext.Transactions.AsNoTracking()
            .Where(transaction => transaction.TransactionDate >= start && transaction.TransactionDate < end && transaction.Type != TransactionType.Transfer && transaction.Type != TransactionType.DebtEntry)
            .Select(transaction => new { transaction.SourceCurrency, transaction.Type, transaction.Amount, transaction.AdjustmentDirection, transaction.DebtOperationType })
            .ToListAsync(cancellationToken);
        var currencies = transactionRows.Where(transaction => TransactionSemantics.Resolve(transaction.Type, transaction.Amount, adjustmentDirection: transaction.AdjustmentDirection, debtOperationType: transaction.DebtOperationType).ReportingClassification != TransactionReportingClassification.Excluded)
            .Select(transaction => transaction.SourceCurrency).Distinct().ToList();
        return Currencies.All.Where(definition => currencies.Contains(definition.Code)).Select(definition => definition.Code).ToList();
    }

    public async Task<StatisticsSummary> GetSummaryAsync(int year, int month, string currency, CancellationToken cancellationToken)
    {
        ValidatePeriod(year, month);
        var selectedStart = new DateOnly(year, month, 1);
        var trendStart = selectedStart.AddMonths(-11);
        var end = selectedStart.AddMonths(1);
        var transactions = await dbContext.Transactions.AsNoTracking()
            .Where(transaction => transaction.TransactionDate >= trendStart && transaction.TransactionDate < end && transaction.Type != TransactionType.Transfer && transaction.SourceCurrency == currency)
            .Select(transaction => new StatisticsTransactionRow(
                transaction.TransactionDate,
                transaction.Type,
                transaction.Amount,
                transaction.AdjustmentDirection,
                transaction.DebtOperationType,
                transaction.CategoryId,
                transaction.Category != null ? transaction.Category.Name : null,
                transaction.Category != null ? transaction.Category.Icon : null,
                transaction.Category != null ? transaction.Category.ParentCategoryId : null,
                transaction.Category != null && transaction.Category.ParentCategory != null ? transaction.Category.ParentCategory.Name : null,
                transaction.Category != null && transaction.Category.ParentCategory != null ? transaction.Category.ParentCategory.Icon : null))
            .ToListAsync(cancellationToken);
        var selected = transactions.Where(transaction => transaction.TransactionDate >= selectedStart).ToList();
        var selectedTotals = PeriodCalculator.Calculate(selected.Select(ToTransaction));
        var accounts = await accountService.GetAllAsync(cancellationToken);
        var balances = await accountService.GetCurrentBalancesAsync(cancellationToken);
        var recurringTemplates = await dbContext.RecurringTransactions.AsNoTracking()
            .Where(template => template.Enabled && template.Type == TransactionType.Expense && template.Account.Currency == currency && template.CategoryId != null && template.FirstOccurrence < end && (template.LastOccurrence == null || template.LastOccurrence >= selectedStart))
            .Select(template => new
            {
                Template = template,
                template.Account.Currency,
                MainCategoryId = template.Category!.ParentCategoryId ?? template.CategoryId!.Value,
                MainCategoryName = template.Category!.ParentCategory != null ? template.Category.ParentCategory.Name : template.Category.Name,
                MainCategoryIcon = template.Category.ParentCategory != null ? template.Category.ParentCategory.Icon : template.Category.Icon
            })
            .ToListAsync(cancellationToken);

        var recurringExpenses = recurringTemplates.Select(item => new
            {
                item.MainCategoryName,
                item.MainCategoryIcon,
                item.MainCategoryId,
                item.Currency,
                OccurrenceCount = RecurringSchedule.GetOccurrences(item.Template, selectedStart, end.AddDays(-1)).Count,
                item.Template.Amount
            })
            .Where(item => item.OccurrenceCount > 0)
            .GroupBy(item => new { item.MainCategoryId, item.MainCategoryName, item.MainCategoryIcon, item.Currency })
            .Select(group => new StatisticsRecurringExpense(group.Key.MainCategoryName, group.Key.MainCategoryIcon, group.Key.Currency, group.Sum(item => item.OccurrenceCount), group.Sum(item => item.Amount * item.OccurrenceCount)))
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.MainCategoryName)
            .ToList();

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
            ExpenseMainCategoryTotals(selected),
            accounts.Select(account => new StatisticsAccountBalance(account.Id, account.Name, account.Currency, balances[account.Id])).ToList(),
            trend,
            recurringExpenses);
    }

    private static IReadOnlyList<StatisticsCategoryTotal> CategoryTotals(IEnumerable<StatisticsTransactionRow> transactions, TransactionType type)
    {
        return transactions.Where(item => IsCategoryType(item, type))
            .GroupBy(item => new { item.CategoryId, Name = CategoryBreakdownName(item), Icon = CategoryIcon(item) })
            .Select(group => new StatisticsCategoryTotal(group.Key.CategoryId, group.Key.Name, group.Sum(item => item.Amount), group.Key.Icon, type == TransactionType.Income ? CategoryType.Income : CategoryType.Expense))
            .OrderByDescending(item => item.Amount)
            .ToList();
    }

    private static IReadOnlyList<StatisticsMainCategoryTotal> ExpenseMainCategoryTotals(IEnumerable<StatisticsTransactionRow> transactions)
    {
        return transactions.Where(item => IsCategoryType(item, TransactionType.Expense))
            .GroupBy(item => new
            {
                CategoryId = item.ParentCategoryId ?? item.CategoryId,
                Name = CategoryName(item),
                Icon = CategoryIcon(item)
            })
            .Select(group => new StatisticsMainCategoryTotal(
                group.Key.CategoryId,
                group.Key.Name,
                group.Sum(item => item.Amount),
                group.Key.Icon,
                group.GroupBy(item => new
                    {
                        CategoryId = item.ParentCategoryId is not null ? item.CategoryId : null,
                        Name = item.ParentCategoryId is not null ? item.CategoryName! : item.CategoryId is null ? CategoryName(item) : "Direct"
                    })
                    .Select(subcategory => new StatisticsSubcategoryTotal(subcategory.Key.CategoryId, subcategory.Key.Name, subcategory.Sum(item => item.Amount)))
                    .OrderByDescending(item => item.Amount)
                    .ThenBy(item => item.Name)
                    .ToList()))
            .OrderByDescending(item => item.Amount)
            .ThenBy(item => item.Name)
            .ToList();
    }

    private static bool IsCategoryType(StatisticsTransactionRow item, TransactionType type)
    {
        // Category reporting intentionally groups adjustments with their matching income/expense side.
        var classification = TransactionSemantics.Resolve(item.Type, item.Amount, adjustmentDirection: item.AdjustmentDirection, debtOperationType: item.DebtOperationType).ReportingClassification;
        return type == TransactionType.Income
            ? classification is TransactionReportingClassification.Income or TransactionReportingClassification.AdjustmentIncrease
            : classification is TransactionReportingClassification.Expense or TransactionReportingClassification.AdjustmentDecrease;
    }

    private static string CategoryName(StatisticsTransactionRow item)
    {
        if (item.Type == TransactionType.Adjustment) return item.AdjustmentDirection == AdjustmentDirection.Decrease ? TransactionIcons.AdjustmentDecreaseDisplayName : TransactionIcons.AdjustmentIncreaseDisplayName;
        if (item.DebtOperationType is DebtOperationType.Payment or DebtOperationType.EarlyRepayment) return "Loan repayment";
        if (item.DebtOperationType == DebtOperationType.ReceivedRepayment) return "Received repayment";
        return item.ParentCategoryName ?? item.CategoryName ?? "Uncategorized";
    }

    private static string CategoryBreakdownName(StatisticsTransactionRow item)
    {
        if (item.Type == TransactionType.Adjustment || item.DebtOperationType is not null) return CategoryName(item);
        return item.CategoryName ?? "Uncategorized";
    }

    private static string? CategoryIcon(StatisticsTransactionRow item)
    {
        if (item.Type == TransactionType.Adjustment) return item.AdjustmentDirection == AdjustmentDirection.Decrease ? TransactionIcons.AdjustmentDecreaseDisplayName : TransactionIcons.AdjustmentIncreaseDisplayName;
        return item.ParentCategoryIcon ?? item.CategoryIcon;
    }

    private record StatisticsTransactionRow(DateOnly TransactionDate, TransactionType Type, decimal Amount, AdjustmentDirection? AdjustmentDirection, DebtOperationType? DebtOperationType, Guid? CategoryId, string? CategoryName, string? CategoryIcon, Guid? ParentCategoryId, string? ParentCategoryName, string? ParentCategoryIcon);

    private static Models.Entities.Transaction ToTransaction(StatisticsTransactionRow row) => new() { TransactionDate = row.TransactionDate, Type = row.Type, Amount = row.Amount, AdjustmentDirection = row.AdjustmentDirection, DebtOperationType = row.DebtOperationType };

    private static void ValidatePeriod(int year, int month)
    {
        if (year < 1 || month is < 1 or > 12) throw new BusinessRuleException("The selected month is invalid.");
    }
}
