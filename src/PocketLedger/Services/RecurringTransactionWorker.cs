using Microsoft.EntityFrameworkCore;
using Npgsql;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public sealed class RecurringTransactionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<RecurringTransactionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueOccurrencesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Recurring transaction processing failed.");
            }

            await Task.Delay(GetDelayUntilNextCheck(), timeProvider, stoppingToken);
        }
    }

    private async Task ProcessDueOccurrencesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PocketLedgerDbContext>();
        var today = BudapestDate.Today(timeProvider);
        var templates = await dbContext.RecurringTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(template => template.Enabled && template.FirstOccurrence <= today && (template.LastOccurrence == null || template.LastOccurrence >= template.AutomationStartsOn))
            .ToListAsync(cancellationToken);

        foreach (var template in templates)
        {
            var start = template.AutomationStartsOn > template.FirstOccurrence ? template.AutomationStartsOn : template.FirstOccurrence;
            var occurrenceDates = RecurringSchedule.GetOccurrences(template, start, today);
            if (occurrenceDates.Count == 0) continue;

            var processedDates = await dbContext.RecurringTransactionOccurrences.IgnoreQueryFilters().AsNoTracking()
                .Where(occurrence => occurrence.RecurringTransactionId == template.Id && occurrence.OccurrenceDate >= start && occurrence.OccurrenceDate <= today)
                .Select(occurrence => occurrence.OccurrenceDate)
                .ToHashSetAsync(cancellationToken);

            foreach (var occurrenceDate in occurrenceDates.Where(date => !processedDates.Contains(date)))
            {
                await CreateOccurrenceAsync(dbContext, template, occurrenceDate, cancellationToken);
            }
        }
    }

    private async Task CreateOccurrenceAsync(PocketLedgerDbContext dbContext, RecurringTransaction template, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (template.DebtId is not null) await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({template.DebtId.Value.ToString()}, 0))", cancellationToken);
        Transaction transaction;
        if (template.DebtId is not null)
        {
            transaction = await new DebtService(dbContext, timeProvider).AddAutomaticOperationAsync(template, occurrenceDate, cancellationToken);
            transaction.OwnerId = template.OwnerId;
        }
        else
        {
            transaction = new Transaction
            {
                Id = Guid.NewGuid(), OwnerId = template.OwnerId, Type = template.Type, AccountId = template.AccountId, Amount = template.Amount,
                AdjustmentDirection = template.AdjustmentDirection, TransactionDate = occurrenceDate, CategoryId = template.CategoryId, Note = template.Note
            };
            dbContext.Transactions.Add(transaction);
        }
        var occurrence = new RecurringTransactionOccurrence
        {
            Id = Guid.NewGuid(), OwnerId = template.OwnerId, RecurringTransactionId = template.Id, OccurrenceDate = occurrenceDate, TransactionId = transaction.Id
        };
        dbContext.RecurringTransactionOccurrences.Add(occurrence);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);
            logger.LogInformation("Created recurring transaction {RecurringTransactionId} for {OccurrenceDate}.", template.Id, occurrenceDate);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.Entry(transaction).State = EntityState.Detached;
            dbContext.Entry(occurrence).State = EntityState.Detached;
            dbContext.ChangeTracker.Clear();
            logger.LogWarning(exception, "Recurring occurrence {RecurringTransactionId} for {OccurrenceDate} was not created, most likely because another instance processed it.", template.Id, occurrenceDate);
        }
    }

    private TimeSpan GetDelayUntilNextCheck()
    {
        var now = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), BudapestDate.TimeZone);
        var nextDate = DateOnly.FromDateTime(now.DateTime).AddDays(1);
        var nextLocal = nextDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocal, BudapestDate.TimeZone);
        var delay = nextUtc - timeProvider.GetUtcNow().UtcDateTime;
        return delay > TimeSpan.Zero && delay < TimeSpan.FromMinutes(1) ? delay : TimeSpan.FromMinutes(1);
    }
}
