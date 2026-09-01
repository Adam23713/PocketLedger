using Microsoft.EntityFrameworkCore;

namespace PocketLedger.Data;

internal sealed class CrossTenantPocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options) : PocketLedgerDbContext(options, true);

public interface ICrossTenantPocketLedgerDbContextFactory
{
    PocketLedgerDbContext CreateDbContext();
}

public sealed class CrossTenantPocketLedgerDbContextFactory(DbContextOptions<PocketLedgerDbContext> options) : ICrossTenantPocketLedgerDbContextFactory
{
    public PocketLedgerDbContext CreateDbContext() => new CrossTenantPocketLedgerDbContext(options);
}
