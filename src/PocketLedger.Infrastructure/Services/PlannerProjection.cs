using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public static class PlannerProjection
{
    public static PlannerMonth Calculate(DateOnly month, DateOnly today, IReadOnlyList<Account> accounts, IReadOnlyList<Transaction> transactions, IReadOnlyList<PlannerItem> plans, IReadOnlyList<RecurringTransaction> templates, IReadOnlyList<RecurringTransactionOccurrence> occurrences, IReadOnlyList<Debt>? debts = null)
    {
        var end = month.AddMonths(1);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var accountMap = accounts.ToDictionary(account => account.Id);
        var events = new List<PlannerEvent>();
        foreach (var transaction in transactions.Where(item => item.TransactionDate < end))
            events.Add(Event(transaction, PlannerEventSource.Actual, transaction.Id, transaction.Amount, transaction.SourceCurrency));
        foreach (var plan in plans)
        {
            var transaction = new Transaction { Type = plan.Type, AccountId = plan.AccountId, TargetAccountId = plan.TargetAccountId, Amount = plan.AccountAmount, TargetAmount = plan.TargetAmount, Category = plan.Category, Note = plan.Note };
            events.Add(Event(transaction, PlannerEventSource.Manual, plan.Id, plan.Amount, plan.Currency) with { Date = plan.PlannedDate });
        }
        var processed = occurrences.Select(item => (item.RecurringTransactionId, item.OccurrenceDate)).ToHashSet();
        var remainingDebts = (debts ?? []).Where(debt => debt.Status == DebtStatus.Active).ToDictionary(debt => debt.Id, debt => DebtBalanceCalculator.Calculate(debt.OriginalAmount, transactions.Where(item => item.DebtId == debt.Id)));
        var scheduled = templates.Where(item => item.Enabled).SelectMany(template =>
        {
            // Include delayed current-month occurrences, but never resurrect processed/deleted ones.
            var from = template.AutomationStartsOn > currentMonth ? template.AutomationStartsOn : currentMonth;
            return RecurringSchedule.GetOccurrences(template, from, end.AddDays(-1)).Select(date => (Template: template, Date: date));
        }).OrderBy(item => item.Date).ThenBy(item => item.Template.Id);
        foreach (var occurrence in scheduled)
        {
            var template = occurrence.Template;
            if (processed.Contains((template.Id, occurrence.Date))) continue;
            var amount = template.Amount;
            if (template.DebtId is { } debtId)
            {
                if (!remainingDebts.TryGetValue(debtId, out var remaining) || remaining <= 0) continue;
                amount = DebtRules.GetAutomaticPaymentAmount(amount, remaining);
                remainingDebts[debtId] += DebtRules.GetDebtDelta(template.DebtOperationType!.Value, amount);
            }
            var transaction = new Transaction { Type = template.Type, AccountId = template.AccountId, Amount = amount, TransactionDate = occurrence.Date, Category = template.Category, Note = template.Note, AdjustmentDirection = template.AdjustmentDirection, DebtOperationType = template.DebtOperationType };
            events.Add(Event(transaction, PlannerEventSource.Recurring, template.Id, amount, accountMap[template.AccountId].Currency));
        }
        var previous = Summarize(month.AddMonths(-1));
        var selected = Summarize(month);
        var currentBalances = accounts.ToDictionary(account => account.Id, account => BalanceCalculator.Calculate(account.Id, account.InitialBalance, transactions));
        var balances = accounts.Select(account => new PlannerAccountBalance(account.Id, account.Name, account.Currency, account.IncludeInMainBalance, currentBalances[account.Id], selected.Opening[account.Id], selected.Closing[account.Id])).ToList();
        var currencies = accounts.Select(account => account.Currency).Concat(selected.Events.Select(item => item.AccountCurrency)).Concat(previous.Events.Select(item => item.AccountCurrency)).Where(currency => !string.IsNullOrEmpty(currency)).Distinct().Order().ToList();
        var totals = currencies.Select(currency =>
        {
            var now = Totals(selected, currency);
            var before = Totals(previous, currency);
            return new PlannerCurrencySummary(currency, now.Income, now.Expenses, now.Closing, now.Available, before.Income, before.Expenses, before.Closing, before.Available);
        }).ToList();
        return new PlannerMonth(month, today, selected.Events, balances, totals, selected.Points);

        PlannerEvent Event(Transaction transaction, PlannerEventSource source, Guid id, decimal originalAmount, string originalCurrency)
        {
            var account = transaction.AccountId is { } accountId ? accountMap.GetValueOrDefault(accountId) : null;
            var target = transaction.TargetAccountId is { } targetId ? accountMap.GetValueOrDefault(targetId) : null;
            var semantics = TransactionSemantics.Resolve(transaction.Type, transaction.Amount, transaction.TargetAmount, transaction.AdjustmentDirection, transaction.DebtOperationType);
            var categoryName = transaction.Category is { ParentCategory: { } parent } category ? $"{parent.Name} / {category.Name}" : transaction.Category?.Name;
            return new PlannerEvent(id, source, transaction.Type, transaction.TransactionDate, transaction.AccountId, account?.Name ?? "—", transaction.TargetAccountId, target?.Name, categoryName, transaction.Note, originalAmount, originalCurrency, transaction.Amount, account?.Currency ?? originalCurrency, transaction.TargetAmount, target?.Currency, semantics.SourceAccountChange, semantics.TargetAccountChange, semantics.ReportingClassification);
        }

        (List<PlannerEvent> Events, Dictionary<Guid, decimal> Opening, Dictionary<Guid, decimal> Closing, List<PlannerBalancePoint> Points) Summarize(DateOnly start)
        {
            var finish = start.AddMonths(1);
            var monthlyPlanIds = plans.Where(item => item.Month == start).Select(item => item.Id).ToHashSet();
            var monthly = events.Where(item => item.Source == PlannerEventSource.Manual ? monthlyPlanIds.Contains(item.Id) : item.Date >= start && item.Date < finish).OrderBy(item => item.Date is null).ThenBy(item => item.Date).ThenBy(item => item.Source).ThenBy(item => item.Id).ToList();
            var opening = accounts.ToDictionary(account => account.Id, account => account.InitialBalance);
            foreach (var item in events.Where(item => item.Date < start && (item.Source == PlannerEventSource.Actual || item.Date >= currentMonth))) Apply(item, opening);
            var closing = new Dictionary<Guid, decimal>(opening);
            var points = new List<PlannerBalancePoint>();
            AddPoints(start, closing);
            var dated = monthly.Where(item => item.Date is not null).GroupBy(item => item.Date!.Value).ToDictionary(group => group.Key, group => group.ToList());
            for (var date = start; date < finish; date = date.AddDays(1))
            {
                if (dated.TryGetValue(date, out var movements)) foreach (var item in movements) Apply(item, closing);
                AddPoints(date, closing);
            }
            return (monthly, opening, closing, points);

            void AddPoints(DateOnly date, Dictionary<Guid, decimal> values)
            {
                foreach (var balance in BalanceCalculator.CalculateMainBalance(accounts.Select(account => (account.Currency, values[account.Id], account.IncludeInMainBalance))))
                    points.Add(new PlannerBalancePoint(date, balance.Currency, balance.Amount));
            }
        }

        (decimal Income, decimal Expenses, decimal Closing, decimal Available) Totals((List<PlannerEvent> Events, Dictionary<Guid, decimal> Opening, Dictionary<Guid, decimal> Closing, List<PlannerBalancePoint> Points) data, string currency)
        {
            var closing = BalanceCalculator.CalculateMainBalance(accounts.Select(account => (account.Currency, data.Closing[account.Id], account.IncludeInMainBalance))).SingleOrDefault(item => item.Currency == currency)?.Amount ?? 0;
            var income = data.Events.Where(item => item.AccountCurrency == currency && item.Classification == TransactionReportingClassification.Income).Sum(item => item.AccountAmount);
            var expense = data.Events.Where(item => item.AccountCurrency == currency && item.Classification == TransactionReportingClassification.Expense).Sum(item => item.AccountAmount);
            var reserves = data.Events.Where(item => item.Date is null && item.AccountCurrency == currency && item.Classification == TransactionReportingClassification.Expense && item.AccountId is { } id && accountMap[id].IncludeInMainBalance).Sum(item => item.AccountAmount);
            return (income, expense, closing, closing - reserves);
        }
    }

    private static void Apply(PlannerEvent item, Dictionary<Guid, decimal> balances)
    {
        if (item.AccountId is { } source && balances.ContainsKey(source)) balances[source] += item.SourceChange;
        if (item.TargetAccountId is { } target && balances.ContainsKey(target)) balances[target] += item.TargetChange;
    }
}
