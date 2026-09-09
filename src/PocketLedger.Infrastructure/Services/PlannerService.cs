using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class PlannerService(PocketLedgerDbContext dbContext, IUserContextService userContext) : IPlannerService
{
    public async Task<PlannerMonth> GetMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        var selected = ValidateMonth(year, month);
        var today = await userContext.TodayAsync(cancellationToken);
        if (selected > new DateOnly(today.Year, today.Month, 1).AddYears(10)) throw new BusinessRuleException("Plans can be viewed up to ten years ahead.");
        await EnsureMonthAsync(selected, today, cancellationToken);
        var record = await dbContext.PlannerMonths.SingleOrDefaultAsync(item => item.Month == selected, cancellationToken);
        return record is null ? new PlannerMonth(selected, today, [], [], [], [], true, false) : PlannerHistory.Deserialize<PlannerMonth>(record.SnapshotJson) with { Today = today };
    }

    public async Task UpdateOpeningBalanceAsync(int year, int month, Guid accountId, PlannerOpeningBalanceInput input, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await dbContext.LockPlannerOwnerAsync(dbContext.PlannerOwnerId, cancellationToken);

        var selected = ValidateMonth(year, month);
        await RequireEditableAsync(selected, cancellationToken);
        if (!await dbContext.Accounts.AnyAsync(item => item.Id == accountId, cancellationToken)) throw new EntityNotFoundException("Account not found.");
        if (input.Amount is { } amount && (amount is < -999999999999999.9999m or > 999999999999999.9999m || decimal.Round(amount, 4) != amount))
            throw new BusinessRuleException("The opening balance supports at most four decimal places.");
        var record = await dbContext.PlannerMonths.SingleAsync(item => item.Month == selected, cancellationToken);
        var settings = PlannerHistory.Deserialize<List<PlannerOpeningBalance>>(record.OpeningBalancesJson);
        var previous = settings.SingleOrDefault(item => item.AccountId == accountId);
        var snapshot = PlannerHistory.Deserialize<PlannerMonth>(record.SnapshotJson);
        var follow = input.UseCurrentBalance ?? previous?.UseCurrentBalance ?? true;
        var value = follow ? previous?.Amount : input.Amount ?? previous?.Amount ?? snapshot.Accounts.Single(item => item.Id == accountId).OpeningBalance;
        settings.RemoveAll(item => item.AccountId == accountId);
        settings.Add(new PlannerOpeningBalance(accountId, follow, value, input.IncludeInBalance ?? previous?.IncludeInBalance ?? true));
        record.OpeningBalancesJson = PlannerHistory.Serialize(settings);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PlannerItemInput?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await dbContext.PlannerItems.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return item is null ? null : ToInput(item);
    }

    public async Task<Guid> CreateAsync(PlannerItemInput input, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await dbContext.LockPlannerOwnerAsync(dbContext.PlannerOwnerId, cancellationToken);

        await RequireEditableAsync(input.Month, cancellationToken);
        await ValidateAsync(input, cancellationToken);
        var item = new PlannerItem { Id = Guid.NewGuid() };
        Apply(item, input);
        dbContext.PlannerItems.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return item.Id;
    }

    public async Task UpdateAsync(Guid id, PlannerItemInput input, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await dbContext.LockPlannerOwnerAsync(dbContext.PlannerOwnerId, cancellationToken);

        var item = await dbContext.PlannerItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Planner item not found.");
        await RequireEditableAsync(item.Month, cancellationToken);
        await RequireEditableAsync(input.Month, cancellationToken);
        await ValidateAsync(input, cancellationToken);
        Apply(item, input);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await dbContext.LockPlannerOwnerAsync(dbContext.PlannerOwnerId, cancellationToken);

        var item = await dbContext.PlannerItems.SingleOrDefaultAsync(item => item.Id == id, cancellationToken) ?? throw new EntityNotFoundException("Planner item not found.");
        await RequireEditableAsync(item.Month, cancellationToken);
        dbContext.PlannerItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task RequireEditableAsync(DateOnly month, CancellationToken token)
    {
        ValidateMonth(month.Year, month.Month);
        if (month.Day != 1) throw new BusinessRuleException("The plan month must be the first day of the month.");
        var today = await userContext.TodayAsync(token);
        if (month < new DateOnly(today.Year, today.Month, 1)) throw new BusinessRuleException("Closed months are read-only.");
        if (month > new DateOnly(today.Year, today.Month, 1).AddYears(10)) throw new BusinessRuleException("Plans can be viewed up to ten years ahead.");
        await EnsureMonthAsync(month, today, token);
        if (await dbContext.PlannerMonths.AnyAsync(item => item.Month == month && item.IsClosed, token)) throw new BusinessRuleException("Closed months are read-only.");
    }

    private async Task EnsureMonthAsync(DateOnly month, DateOnly today, CancellationToken token)
    {
        await using var transaction = dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null ? await dbContext.Database.BeginTransactionAsync(token) : null;
        await dbContext.LockPlannerOwnerAsync(dbContext.PlannerOwnerId, token);
        var skipHistory = dbContext.SkipPlannerHistory;
        try
        {
            dbContext.SkipPlannerHistory = true;
            await PlannerHistory.RefreshAsync(dbContext, dbContext.PlannerOwnerId, today, month, token);
            await dbContext.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
        finally { dbContext.SkipPlannerHistory = skipHistory; }
    }

    private static DateOnly ValidateMonth(int year, int month)
    {
        if (year is < 2 or > 9998 || month is < 1 or > 12) throw new BusinessRuleException("The selected month is invalid.");
        return new DateOnly(year, month, 1);
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
        item.CopyDay = input.PlannedDate?.Day;
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
