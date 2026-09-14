using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PocketLedger.Security;

namespace PocketLedger.Data;

internal sealed class CrossTenantPocketLedgerDbContext : PocketLedgerDbContext
{
    public CrossTenantPocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options, DatabaseEncryption? encryption = null) : base(options, true)
        => Encryption = encryption;
}

internal interface IRecurringTransactionProcessingDbContextFactory
{
    PocketLedgerDbContext CreateDbContext();
}

internal sealed class RecurringTransactionProcessingDbContextFactory(DbContextOptions<PocketLedgerDbContext> options, DatabaseEncryption? encryption = null) : IRecurringTransactionProcessingDbContextFactory
{
    public PocketLedgerDbContext CreateDbContext() => new CrossTenantPocketLedgerDbContext(options, encryption);
}

public static class RecurringTransactionProcessingServiceCollectionExtensions
{
    public static IServiceCollection AddRecurringTransactionProcessingDataAccess(this IServiceCollection services)
        => services.AddScoped<IRecurringTransactionProcessingDbContextFactory, RecurringTransactionProcessingDbContextFactory>();
}
