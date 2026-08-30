using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;
using PocketLedger.Services;

namespace PocketLedger.Data;

public class PocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options, ICurrentUser? currentUser = null) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<RecurringTransactionOccurrence> RecurringTransactionOccurrences => Set<RecurringTransactionOccurrence>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<UserCurrencyFormat> UserCurrencyFormats => Set<UserCurrencyFormat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PocketLedgerDbContext).Assembly);

        modelBuilder.Entity<Account>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
        modelBuilder.Entity<Category>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
        modelBuilder.Entity<Transaction>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
        modelBuilder.Entity<RecurringTransaction>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
        modelBuilder.Entity<RecurringTransactionOccurrence>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
        modelBuilder.Entity<Debt>().HasQueryFilter(entity => currentUser == null || !currentUser.IsAuthenticated || entity.OwnerId == currentUser.UserId);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser?.IsAuthenticated == true)
        {
            foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State == EntityState.Added))
            {
                switch (entry.Entity)
                {
                    case Account account: account.OwnerId = currentUser.UserId; break;
                    case Category category: category.OwnerId = currentUser.UserId; break;
                    case Transaction transaction: transaction.OwnerId = currentUser.UserId; break;
                    case RecurringTransaction recurring: recurring.OwnerId = currentUser.UserId; break;
                    case RecurringTransactionOccurrence occurrence: occurrence.OwnerId = currentUser.UserId; break;
                    case Debt debt: debt.OwnerId = currentUser.UserId; break;
                }
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
