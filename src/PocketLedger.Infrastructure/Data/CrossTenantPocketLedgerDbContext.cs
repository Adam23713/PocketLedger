using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PocketLedger.Data;

internal sealed class CrossTenantPocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options) : PocketLedgerDbContext(options, true);

internal interface IRecurringTransactionProcessingDbContextFactory
{
    PocketLedgerDbContext CreateDbContext();
}

internal sealed class RecurringTransactionProcessingDbContextFactory(DbContextOptions<PocketLedgerDbContext> options) : IRecurringTransactionProcessingDbContextFactory
{
    public PocketLedgerDbContext CreateDbContext() => new CrossTenantPocketLedgerDbContext(options);
}

public static class RecurringTransactionProcessingServiceCollectionExtensions
{
    public static IServiceCollection AddRecurringTransactionProcessingDataAccess(this IServiceCollection services)
        => services.AddScoped<IRecurringTransactionProcessingDbContextFactory, RecurringTransactionProcessingDbContextFactory>();
}
