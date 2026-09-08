using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Data;

namespace PocketLedger.Services;

public sealed class PlannerMonthWorker(IServiceScopeFactory scopeFactory, IUserDateProvider dates, IOptions<UserDateOptions> options, ILogger<PlannerMonthWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CloseDueMonthsAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Monthly planner rollover failed; it will be retried."); }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal async Task CloseDueMonthsAsync(CancellationToken token)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IRecurringTransactionProcessingDbContextFactory>();
        await using var discovery = factory.CreateDbContext();
        var owners = await discovery.PlannerMonths.Where(item => !item.IsClosed).Select(item => item.OwnerId).Distinct().ToListAsync(token);
        foreach (var owner in owners)
        {
            await using var db = factory.CreateDbContext();
            var zone = await db.UserPreferences.Where(item => item.UserId == owner).Select(item => item.TimeZoneId).SingleOrDefaultAsync(token) ?? options.Value.DefaultTimeZoneId;
            var today = dates.Today(zone);
            if (!await db.PlannerMonths.AnyAsync(item => item.OwnerId == owner && !item.IsClosed && item.Month < new DateOnly(today.Year, today.Month, 1), token)) continue;
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(token) : null;
            await db.LockPlannerOwnerAsync(owner, token);
            db.SkipPlannerHistory = true;
            await PlannerHistory.RefreshAsync(db, owner, today, null, token);
            await db.SaveChangesAsync(token);
            if (transaction is not null) await transaction.CommitAsync(token);
        }
    }
}
