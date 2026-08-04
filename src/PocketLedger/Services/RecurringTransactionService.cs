using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class RecurringTransactionService(PocketLedgerDbContext dbContext, TimeProvider timeProvider) : IRecurringTransactionService
{
    public async Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await BaseQuery().OrderByDescending(template => template.Enabled).ThenBy(template => template.FirstOccurrence).ThenBy(template => template.Id).ToListAsync(cancellationToken);
    }

    public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return BaseQuery().SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
    }

    public async Task<RecurringTransaction> CreateAsync(RecurringTransaction template, CancellationToken cancellationToken)
    {
        await ValidateAsync(template, cancellationToken);
        template.Id = template.Id == Guid.Empty ? Guid.NewGuid() : template.Id;
        template.AutomationStartsOn = BudapestDate.Today(timeProvider);
        dbContext.RecurringTransactions.Add(template);
        await dbContext.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task UpdateAsync(RecurringTransaction template, CancellationToken cancellationToken)
    {
        await ValidateAsync(template, cancellationToken);
        var existing = await dbContext.RecurringTransactions.SingleOrDefaultAsync(item => item.Id == template.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Recurring transaction not found.");
        var scheduleChanged = existing.FirstOccurrence != template.FirstOccurrence || existing.LastOccurrence != template.LastOccurrence || existing.Frequency != template.Frequency;
        if (scheduleChanged || !existing.Enabled && template.Enabled) existing.AutomationStartsOn = BudapestDate.Today(timeProvider);
        existing.Type = template.Type;
        existing.AccountId = template.AccountId;
        existing.CategoryId = template.CategoryId;
        existing.Amount = template.Amount;
        existing.AdjustmentDirection = template.AdjustmentDirection;
        existing.Note = template.Note;
        existing.FirstOccurrence = template.FirstOccurrence;
        existing.LastOccurrence = template.LastOccurrence;
        existing.Frequency = template.Frequency;
        existing.Enabled = template.Enabled;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await dbContext.RecurringTransactions.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Recurring transaction not found.");
        dbContext.RecurringTransactions.Remove(template);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<RecurringTransaction> BaseQuery()
    {
        return dbContext.RecurringTransactions.AsNoTracking().Include(template => template.Account).Include(template => template.Category).ThenInclude(category => category!.ParentCategory);
    }

    private async Task ValidateAsync(RecurringTransaction template, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == template.AccountId, cancellationToken);
        Category? category = null;
        if (template.CategoryId is not null)
        {
            category = await dbContext.Categories.AsNoTracking().SingleOrDefaultAsync(item => item.Id == template.CategoryId, cancellationToken);
        }

        RecurringTransactionRules.Validate(template, account, category);
        template.Note = string.IsNullOrWhiteSpace(template.Note) ? null : template.Note.Trim();
    }
}
