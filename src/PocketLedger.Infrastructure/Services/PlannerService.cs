using System.Data;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class PlannerService(PocketLedgerDbContext dbContext, IUserContextService userContext) : IPlannerService
{
    public async Task<PlannerMonth> GetMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        if (year is < 2 or > 9998 || month is < 1 or > 12) throw new BusinessRuleException("The selected month is invalid.");
        var selected = new DateOnly(year, month, 1);
        var today = await userContext.TodayAsync(cancellationToken);
        // Bound recurrence expansion while still allowing historical inspection and long-term planning.
        if (selected > new DateOnly(today.Year, today.Month, 1).AddYears(10)) throw new BusinessRuleException("Plans can be viewed up to ten years ahead.");
        // Keep actual transactions and processed occurrences in the same snapshot while the worker runs.
        await using var snapshot = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL" ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, cancellationToken)
            : null;
        var end = selected.AddMonths(1);
        var accounts = await dbContext.Accounts.AsNoTracking().OrderBy(account => account.DisplayOrder).ThenBy(account => account.Name).ToListAsync(cancellationToken);
        var transactions = await dbContext.Transactions.AsNoTracking().Include(item => item.Category).ThenInclude(category => category!.ParentCategory).ToListAsync(cancellationToken);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var from = selected.AddMonths(-1) < currentMonth ? selected.AddMonths(-1) : currentMonth;
        var plans = await dbContext.PlannerItems.AsNoTracking().Include(item => item.Category).ThenInclude(category => category!.ParentCategory)
            .Where(item => item.Month >= from && item.Month < end).ToListAsync(cancellationToken);
        var templates = await dbContext.RecurringTransactions.AsNoTracking().Include(item => item.Category).ThenInclude(category => category!.ParentCategory)
            .Where(item => item.Enabled && item.FirstOccurrence < end && (item.LastOccurrence == null || item.LastOccurrence >= from)).ToListAsync(cancellationToken);
        var occurrences = await dbContext.RecurringTransactionOccurrences.AsNoTracking().Where(item => item.OccurrenceDate >= from && item.OccurrenceDate < end).ToListAsync(cancellationToken);
        var debts = await dbContext.Debts.AsNoTracking().ToListAsync(cancellationToken);
        return PlannerProjection.Calculate(selected, today, accounts, transactions, plans, templates, occurrences, debts);
    }

    public async Task<PlannerItemInput?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.PlannerItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return item is null ? null : ToInput(item);
    }

    public async Task<Guid> CreateAsync(PlannerItemInput input, CancellationToken cancellationToken)
    {
        await ValidateAsync(input, cancellationToken);
        var item = new PlannerItem { Id = Guid.NewGuid() };
        Apply(item, input);
        dbContext.PlannerItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public async Task UpdateAsync(Guid id, PlannerItemInput input, CancellationToken cancellationToken)
    {
        var item = await dbContext.PlannerItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Planner item not found.");
        await ValidateAsync(input, cancellationToken);
        Apply(item, input);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.PlannerItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Planner item not found.");
        dbContext.PlannerItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateAsync(PlannerItemInput input, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.SingleOrDefaultAsync(item => item.Id == input.AccountId, cancellationToken);
        var target = input.TargetAccountId is { } targetId ? await dbContext.Accounts.SingleOrDefaultAsync(item => item.Id == targetId, cancellationToken) : null;
        var category = input.CategoryId is { } categoryId ? await dbContext.Categories.SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken) : null;
        PlannerRules.Validate(input, account, target, category);
    }

    internal static PlannerItemInput ToInput(PlannerItem item) => new(item.Month, item.PlannedDate, item.Type, item.AccountId, item.TargetAccountId, item.CategoryId, item.Amount, item.Currency, item.AccountAmount, item.TargetAmount, item.Note);

    internal static void Apply(PlannerItem item, PlannerItemInput input)
    {
        item.Month = input.Month;
        item.PlannedDate = input.PlannedDate;
        item.Type = input.Type;
        item.AccountId = input.AccountId;
        item.TargetAccountId = input.TargetAccountId;
        item.CategoryId = input.CategoryId;
        item.Amount = input.Amount;
        item.Currency = input.Currency.Trim().ToUpperInvariant();
        item.AccountAmount = input.AccountAmount;
        item.TargetAmount = input.TargetAmount;
        item.Note = input.Note?.Trim();
    }
}
