using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public sealed class RecurringTransactionWorker(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, IUserDateProvider userDates, Microsoft.Extensions.Options.IOptions<UserDateOptions> dateOptions, ILogger<RecurringTransactionWorker> logger) : BackgroundService
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

    internal async Task ProcessDueOccurrencesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IRecurringTransactionProcessingDbContextFactory>();
        await using var dbContext = dbContextFactory.CreateDbContext();
        var templates = await dbContext.RecurringTransactions.AsNoTracking()
            .Include(template => template.Account)
            .Where(template => template.Enabled && (template.LastOccurrence == null || template.LastOccurrence >= template.AutomationStartsOn))
            .ToListAsync(cancellationToken);

        var timeZones = await dbContext.UserPreferences.AsNoTracking().ToDictionaryAsync(item => item.UserId, item => item.TimeZoneId, cancellationToken);
        foreach (var template in templates)
        {
            try
            {
                var timeZoneId = timeZones.GetValueOrDefault(template.OwnerId, dateOptions.Value.DefaultTimeZoneId);
                var today = userDates.Today(timeZoneId);
                if (template.FirstOccurrence > today) continue;
                var start = template.AutomationStartsOn > template.FirstOccurrence ? template.AutomationStartsOn : template.FirstOccurrence;
                var occurrenceDates = RecurringSchedule.GetOccurrences(template, start, today);
                if (occurrenceDates.Count == 0) continue;

                var processedDates = await dbContext.RecurringTransactionOccurrences.AsNoTracking()
                    .Where(occurrence => occurrence.RecurringTransactionId == template.Id && occurrence.OccurrenceDate >= start && occurrence.OccurrenceDate <= today)
                    .Select(occurrence => occurrence.OccurrenceDate)
                    .ToHashSetAsync(cancellationToken);

                foreach (var occurrenceDate in occurrenceDates.Where(date => !processedDates.Contains(date)))
                {
                    try
                    {
                        await CreateOccurrenceAsync(dbContext, template, occurrenceDate, timeZoneId, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        dbContext.ChangeTracker.Clear();
                        logger.LogError(exception, "Recurring occurrence {RecurringTransactionId} for {OccurrenceDate} failed and will be retried.", template.Id, occurrenceDate);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                dbContext.ChangeTracker.Clear();
                logger.LogError(exception, "Recurring transaction {RecurringTransactionId} could not be evaluated and will be retried.", template.Id);
            }
        }
    }

    private async Task CreateOccurrenceAsync(PocketLedgerDbContext dbContext, RecurringTransaction template, DateOnly occurrenceDate, string timeZoneId, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (template.DebtId is not null) await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({template.DebtId.Value.ToString()}, 0))", cancellationToken);
        Transaction transaction;
        if (template.DebtId is not null)
        {
            transaction = await new DebtService(dbContext).AddAutomaticOperationAsync(template, occurrenceDate, cancellationToken);
            transaction.OwnerId = template.OwnerId;
            transaction.SourceCurrency = template.Account.Currency;
            transaction.OccurredAtUtc = userDates.ToUtc(occurrenceDate, TimeOnly.MinValue, timeZoneId);
        }
        else
        {
            transaction = new Transaction
            {
                Id = Guid.NewGuid(), OwnerId = template.OwnerId, Type = template.Type, AccountId = template.AccountId, Amount = template.Amount,
                AdjustmentDirection = template.AdjustmentDirection, TransactionDate = occurrenceDate, TransactionTime = TimeOnly.MinValue,
                OccurredAtUtc = userDates.ToUtc(occurrenceDate, TimeOnly.MinValue, timeZoneId),
                SourceCurrency = template.Account.Currency, CategoryId = template.CategoryId, Note = template.Note
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
        return TimeSpan.FromMinutes(1);
    }
}
