using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public static class PlannerProjection
{
    public static PlannerMonth Calculate(DateOnly month, DateOnly today, IReadOnlyList<Account> accounts, IReadOnlyDictionary<Guid, decimal> currentBalances, IReadOnlyList<PlannerOpeningBalance> openingBalances, IReadOnlyList<PlannerItem> plans, IReadOnlyList<RecurringTransaction> templates, PlannerMonth? previous = null)
    {
        accounts = accounts.OrderBy(account => account.DisplayOrder).ThenBy(account => account.Name).ThenBy(account => account.Id).ToList();
        var accountMap = accounts.ToDictionary(account => account.Id);
        var settings = openingBalances.ToDictionary(item => item.AccountId);
        bool Included(Guid id) => settings.GetValueOrDefault(id)?.IncludeInBalance ?? true;
        var events = new List<PlannerEvent>();
        foreach (var plan in plans.Where(item => item.Month == month))
            events.Add(CreateEvent(plan.Id, PlannerEventSource.Planned, plan.Type, plan.PlannedDate, plan.AccountId, plan.TargetAccountId, plan.Category, plan.Note, plan.Amount, plan.Currency, plan.AccountAmount, plan.TargetAmount));

        foreach (var template in templates.Where(item => item.Enabled && item.Type is TransactionType.Income or TransactionType.Expense && (item.DebtId == null || item.Debt?.Status == DebtStatus.Active)))
        {
            if (!accountMap.TryGetValue(template.AccountId, out var account)) continue;
            // A schedule is a planning input regardless of whether any actual occurrence was paid.
            foreach (var date in RecurringSchedule.GetOccurrences(template, month, month.AddMonths(1).AddDays(-1)))
                events.Add(CreateEvent(template.Id, PlannerEventSource.Recurring, template.Type, date, template.AccountId, null, template.Category, template.Note ?? template.Debt?.Name, template.Amount, account.Currency, template.Amount, null, template.DebtOperationType));
        }

        events = events.OrderBy(item => item.Date is null).ThenBy(item => item.Date).ThenBy(item => item.Source).ThenBy(item => item.Id).ToList();
        var opening = accounts.ToDictionary(account => account.Id, account =>
        {
            var setting = settings.GetValueOrDefault(account.Id);
            return setting?.UseCurrentBalance == false ? setting.Amount ?? currentBalances.GetValueOrDefault(account.Id) : currentBalances.GetValueOrDefault(account.Id);
        });
        var closing = new Dictionary<Guid, decimal>(opening);
        var points = new List<PlannerBalancePoint>();
        AddPoints(month);
        var dated = events.Where(item => item.Date is not null).GroupBy(item => item.Date!.Value).ToDictionary(group => group.Key, group => group.ToList());
        for (var date = month; date < month.AddMonths(1); date = date.AddDays(1))
        {
            if (dated.TryGetValue(date, out var movements)) foreach (var item in movements) Apply(item);
            AddPoints(date);
        }
        // Undated monthly items affect the final plan, but are never assigned an invented chart date.
        foreach (var item in events.Where(item => item.Date is null && item.Type == TransactionType.Income)) Apply(item);
        var balances = accounts.Select(account =>
        {
            var setting = settings.GetValueOrDefault(account.Id);
            return new PlannerAccountBalance(account.Id, account.Name, account.Currency, Included(account.Id), currentBalances.GetValueOrDefault(account.Id), opening[account.Id], closing[account.Id], setting?.UseCurrentBalance ?? true, setting?.Amount, PocketLedger.Models.AccountIcons.Resolve(account.Icon, account.Type).Id);
        }).ToList();
        var mainBalances = BalanceCalculator.CalculateMainBalance(accounts.Select(account => (account.Currency, closing[account.Id], Included(account.Id))));
        var currencies = accounts.Select(account => account.Currency).Union(previous?.Totals.Select(item => item.Currency) ?? []).Order();
        var totals = currencies.Select(currency =>
        {
            var income = events.Where(item => item.AccountCurrency == currency && item.Classification == TransactionReportingClassification.Income).Sum(item => item.AccountAmount);
            var expenses = events.Where(item => item.AccountCurrency == currency && item.Classification == TransactionReportingClassification.Expense).Sum(item => item.AccountAmount);
            var closingBalance = mainBalances.SingleOrDefault(item => item.Currency == currency)?.Amount ?? 0;
            var reserves = events.Where(item => item.Date is null && item.AccountCurrency == currency && item.Type == TransactionType.Expense && item.AccountId is { } id && Included(id)).Sum(item => item.AccountAmount);
            var before = previous?.Totals.SingleOrDefault(item => item.Currency == currency);
            return new PlannerCurrencySummary(currency, income, expenses, closingBalance, closingBalance - reserves, before?.Income ?? 0, before?.Expenses ?? 0, before?.ClosingBalance ?? 0, before?.Available ?? 0);
        }).ToList();
        return new PlannerMonth(month, today, events, balances, totals, points);

        PlannerEvent CreateEvent(Guid id, PlannerEventSource source, TransactionType type, DateOnly? date, Guid accountId, Guid? targetId, Category? category, string? note, decimal amount, string currency, decimal accountAmount, decimal? targetAmount, DebtOperationType? debtOperation = null)
        {
            var account = accountMap[accountId];
            var target = targetId is { } targetAccountId ? accountMap[targetAccountId] : null;
            var semantics = TransactionSemantics.Resolve(type, accountAmount, targetAmount, debtOperationType: debtOperation);
            var categoryName = category?.ParentCategory is { } parent ? $"{parent.Name} / {category.Name}" : category?.Name;
            return new PlannerEvent(id, source, type, date, accountId, account.Name, targetId, target?.Name, categoryName, note, amount, currency, accountAmount, account.Currency, targetAmount, target?.Currency, semantics.SourceAccountChange, semantics.TargetAccountChange, semantics.ReportingClassification);
        }

        void Apply(PlannerEvent item)
        {
            if (item.AccountId is { } source) closing[source] += item.SourceChange;
            if (item.TargetAccountId is { } target) closing[target] += item.TargetChange;
        }

        void AddPoints(DateOnly date)
        {
            foreach (var balance in BalanceCalculator.CalculateMainBalance(accounts.Select(account => (account.Currency, closing[account.Id], Included(account.Id)))))
                points.Add(new PlannerBalancePoint(date, balance.Currency, balance.Amount));
        }
    }
}
