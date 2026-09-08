using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

internal static class PlannerHistory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    internal static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new BusinessRuleException("The saved plan is invalid.");

    internal static async Task RefreshAsync(PocketLedgerDbContext db, Guid ownerId, DateOnly today, DateOnly? requestedMonth, CancellationToken token, bool includePendingChanges = true)
    {
        List<T> Working<T>(List<T> persisted) where T : class => includePendingChanges ? Merge(db, persisted, ownerId) : persisted;
        var current = new DateOnly(today.Year, today.Month, 1);
        var records = await db.PlannerMonths.Where(item => item.OwnerId == ownerId).OrderBy(item => item.Month).ToListAsync(token);
        records = Merge(db, records, ownerId);
        if (records.Count == 0 && requestedMonth is null) return;
        var accounts = Working(await db.Accounts.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token));
        var categories = Working(await db.Categories.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token)).Select(item => new Category { Id = item.Id, Name = item.Name, Type = item.Type, ParentCategoryId = item.ParentCategoryId }).ToDictionary(item => item.Id);
        foreach (var category in categories.Values) category.ParentCategory = category.ParentCategoryId is { } parentId ? categories.GetValueOrDefault(parentId) : null;
        var debts = Working(await db.Debts.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token)).ToDictionary(item => item.Id);
        var templates = Working(await db.RecurringTransactions.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token)).Select(item => new RecurringTransaction { Id = item.Id, AccountId = item.AccountId, CategoryId = item.CategoryId, DebtId = item.DebtId, Type = item.Type, Amount = item.Amount, Note = item.Note, Enabled = item.Enabled, FirstOccurrence = item.FirstOccurrence, LastOccurrence = item.LastOccurrence, Frequency = item.Frequency, DebtOperationType = item.DebtOperationType }).ToList();
        foreach (var template in templates)
        {
            template.Category = template.CategoryId is { } categoryId ? categories.GetValueOrDefault(categoryId) : null;
            template.Debt = template.DebtId is { } debtId ? debts.GetValueOrDefault(debtId) : null;
        }
        var plans = Working(await db.PlannerItems.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token)).Select(CopyPlan).ToList();
        foreach (var plan in plans) plan.Category = plan.CategoryId is { } id ? categories.GetValueOrDefault(id) : null;

        // Ledger rows are used only by the existing current-balance calculator, never as plan events.
        var ledger = Working(await db.Transactions.AsNoTracking().Where(item => item.OwnerId == ownerId).ToListAsync(token));
        var currentBalances = accounts.ToDictionary(account => account.Id, account => BalanceCalculator.Calculate(account.Id, account.InitialBalance, ledger));
        var last = requestedMonth > current ? requestedMonth.Value : current;
        var first = records.Where(record => record.Month <= current).Select(record => (DateOnly?)record.Month).Max() ?? current;
        if (first > current) first = current;
        for (var month = first; month <= last; month = month.AddMonths(1))
        {
            var record = records.SingleOrDefault(item => item.Month == month);
            if (record is null)
            {
                var previousRecord = records.Where(item => item.Month < month).MaxBy(item => item.Month);
                record = new PlannerMonthRecord { Id = Guid.NewGuid(), OwnerId = ownerId, Month = month, OpeningBalancesJson = previousRecord?.OpeningBalancesJson ?? "[]" };
                db.PlannerMonths.Add(record);
                records.Add(record);
                // An already prepared future month has its own items; never overwrite those edits.
                if (!plans.Any(item => item.Month == month) && previousRecord is not null)
                {
                    foreach (var source in plans.Where(item => item.Month == previousRecord.Month).ToList())
                    {
                        var copy = new PlannerItem { Id = Guid.NewGuid(), OwnerId = ownerId };
                        var day = source.CopyDay ?? source.PlannedDate?.Day;
                        PlannerService.Apply(copy, PlannerService.ToInput(source) with { Month = month, PlannedDate = day is { } value ? new DateOnly(month.Year, month.Month, Math.Min(value, DateTime.DaysInMonth(month.Year, month.Month))) : null });
                        copy.CopyDay = day;
                        db.PlannerItems.Add(copy);
                        var projected = CopyPlan(copy);
                        projected.Category = source.Category;
                        plans.Add(projected);
                    }
                }
            }
        }

        foreach (var record in records.OrderBy(item => item.Month))
        {
            if (record.IsClosed) continue;
            // The last saved state is the final state of a past month. Never rebuild it from live data.
            if (record.Month < current && record.SnapshotJson.Length > 0)
            {
                record.IsClosed = true;
                record.SnapshotJson = Serialize(Deserialize<PlannerMonth>(record.SnapshotJson) with { IsClosed = true });
                continue;
            }
            var previousRecord = records.SingleOrDefault(item => item.Month == record.Month.AddMonths(-1));
            var previous = previousRecord?.SnapshotJson is { Length: > 0 } json ? Deserialize<PlannerMonth>(json) : null;
            var settings = Deserialize<List<PlannerOpeningBalance>>(record.OpeningBalancesJson);
            var projection = PlannerProjection.Calculate(record.Month, today, accounts, currentBalances, settings, plans, templates, previous);
            record.IsClosed = record.Month < current;
            record.SnapshotJson = Serialize(projection with { IsClosed = record.IsClosed });
        }
    }

    private static PlannerItem CopyPlan(PlannerItem source)
    {
        var item = new PlannerItem { Id = source.Id, OwnerId = source.OwnerId };
        PlannerService.Apply(item, PlannerService.ToInput(source));
        item.CopyDay = source.CopyDay;
        return item;
    }

    private static List<T> Merge<T>(PocketLedgerDbContext db, List<T> persisted, Guid ownerId) where T : class
    {
        Guid Id(T item) => (Guid)db.Entry(item).Property("Id").CurrentValue!;
        var result = persisted.ToDictionary(Id);
        foreach (var entry in db.ChangeTracker.Entries<T>().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            if ((Guid)entry.Property("OwnerId").CurrentValue! != ownerId) continue;
            if (entry.State == EntityState.Deleted) result.Remove(Id(entry.Entity));
            else result[Id(entry.Entity)] = entry.Entity;
        }
        return result.Values.ToList();
    }
}
