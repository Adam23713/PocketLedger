using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public sealed class UserDataDeletionService(PocketLedgerDbContext dbContext) : IUserDataDeletionService
{
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var transaction = dbContext.Database.IsRelational() ? await dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await dbContext.RecurringTransactionOccurrences.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.PlannerItems.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.PlannerMonths.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Transactions.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.RecurringTransactions.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Debts.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Goals.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Categories.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteUpdateAsync(update => update.SetProperty(item => item.ParentCategoryId, (Guid?)null), cancellationToken);
        await dbContext.Categories.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Accounts.IgnoreQueryFilters().Where(item => item.OwnerId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserCurrencyFormats.IgnoreQueryFilters().Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserPreferences.IgnoreQueryFilters().Where(item => item.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        if (dbContext.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM financial_cache_revisions WHERE owner_id = {userId}", cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
