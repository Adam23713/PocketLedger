using Microsoft.EntityFrameworkCore;
using PocketLedger.Models.Entities;
using PocketLedger.Services;

namespace PocketLedger.Data;

public class PocketLedgerDbContext : DbContext
{
    private readonly ICurrentUser? currentUser;
    private readonly bool crossTenantAccess;
    private readonly bool hasTenantContext;
    private readonly Guid tenantId;
    private readonly IUserDateProvider plannerDates;
    private readonly string defaultTimeZone;
    internal bool SkipPlannerHistory { get; set; }
    internal Guid PlannerOwnerId => hasTenantContext ? tenantId : throw new InvalidOperationException("An authenticated tenant context is required.");

    public PocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options, ICurrentUser? currentUser = null, IUserDateProvider? plannerDates = null, Microsoft.Extensions.Options.IOptions<UserDateOptions>? dateOptions = null) : base(options)
    {
        this.currentUser = currentUser;
        this.plannerDates = plannerDates ?? new UserDateProvider(TimeProvider.System);
        defaultTimeZone = dateOptions?.Value.DefaultTimeZoneId ?? "UTC";
        hasTenantContext = currentUser?.IsAuthenticated == true;
        tenantId = hasTenantContext ? currentUser!.UserId : Guid.Empty;
    }
    private protected PocketLedgerDbContext(DbContextOptions<PocketLedgerDbContext> options, bool crossTenantAccess) : base(options)
    {
        this.crossTenantAccess = crossTenantAccess;
        plannerDates = new UserDateProvider(TimeProvider.System);
        defaultTimeZone = "UTC";
    }
    public DbSet<PlannerMonthRecord> PlannerMonths => Set<PlannerMonthRecord>();
    public DbSet<PlannerItem> PlannerItems => Set<PlannerItem>();
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

        modelBuilder.Entity<PlannerMonthRecord>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<PlannerItem>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<Account>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<Category>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<Transaction>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<RecurringTransaction>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<RecurringTransactionOccurrence>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
        modelBuilder.Entity<Debt>().HasQueryFilter(entity => crossTenantAccess || hasTenantContext && entity.OwnerId == tenantId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
        => SaveChangesAsync(acceptAllChangesOnSuccess, CancellationToken.None).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(true, cancellationToken);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareFinanceChanges();
        await ValidatePersistedOwnersAsync(cancellationToken);
        await ValidateFinanceReferencesAsync(cancellationToken);
        var owners = FinanceEntries().Select(entry => GetOwnerId(entry.Entity)).Distinct().Order().ToList();
        await using var transaction = !SkipPlannerHistory && owners.Count > 0 && Database.IsRelational() && Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken) : null;
        if (!SkipPlannerHistory)
        {
            foreach (var owner in owners)
            {
                await LockPlannerOwnerAsync(owner, cancellationToken);
                var timeZone = await UserPreferences.Where(item => item.UserId == owner).Select(item => item.TimeZoneId).SingleOrDefaultAsync(cancellationToken) ?? defaultTimeZone;
                var today = plannerDates.Today(timeZone);
                var month = new DateOnly(today.Year, today.Month, 1);
                // Catch up missed boundaries before applying this write to the current plan.
                if (await PlannerMonths.AnyAsync(item => item.OwnerId == owner && !item.IsClosed && item.Month < month, cancellationToken))
                    await PlannerHistory.RefreshAsync(this, owner, today, null, cancellationToken, includePendingChanges: false);
                await PlannerHistory.RefreshAsync(this, owner, today, null, cancellationToken);
            }
        }
        PrepareFinanceChanges();
        await ValidateFinanceReferencesAsync(cancellationToken);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return result;
    }

    internal async Task LockPlannerOwnerAsync(Guid owner, CancellationToken token)
    {
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({owner.ToString()}, 83))", token);
    }

    private void PrepareFinanceChanges()
    {
        var entries = FinanceEntries().ToList();
        if (entries.Count == 0) return;
        if (!crossTenantAccess && currentUser?.IsAuthenticated != true) throw new InvalidOperationException("An authenticated tenant context is required to modify financial data.");
        foreach (var entry in entries)
        {
            if (!crossTenantAccess && entry.State == EntityState.Added) SetOwnerId(entry.Entity, currentUser!.UserId);
            var ownerId = GetOwnerId(entry.Entity);
            if (ownerId == Guid.Empty) throw new BusinessRuleException("Financial data must have an owner.");
            if (!crossTenantAccess && ownerId != currentUser!.UserId) throw new BusinessRuleException("Financial data cannot be assigned to another owner.");
            if (!crossTenantAccess && entry.State is EntityState.Modified or EntityState.Deleted && (Guid)entry.Property(nameof(Account.OwnerId)).OriginalValue! != currentUser!.UserId)
                throw new BusinessRuleException("Financial data owned by another user cannot be modified.");
        }
    }

    private void ValidateFinanceReferences()
    {
        foreach (var entry in FinanceEntries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            foreach (var reference in GetReferences(entry.Entity))
                if (!ReferenceHasOwner(reference.Type, reference.Id, GetOwnerId(entry.Entity))) throw new BusinessRuleException("Referenced financial data must belong to the same owner.");
    }

    private void ValidatePersistedOwners()
    {
        if (crossTenantAccess) return;
        foreach (var entry in FinanceEntries().Where(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            if (!PersistedOwnerMatches(entry.Entity, tenantId)) throw new BusinessRuleException("Financial data owned by another user cannot be modified.");
    }

    private async Task ValidatePersistedOwnersAsync(CancellationToken cancellationToken)
    {
        if (crossTenantAccess) return;
        foreach (var entry in FinanceEntries().Where(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            if (!await PersistedOwnerMatchesAsync(entry.Entity, tenantId, cancellationToken)) throw new BusinessRuleException("Financial data owned by another user cannot be modified.");
    }

    private async Task ValidateFinanceReferencesAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in FinanceEntries().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
            foreach (var reference in GetReferences(entry.Entity))
                if (!await ReferenceHasOwnerAsync(reference.Type, reference.Id, GetOwnerId(entry.Entity), cancellationToken)) throw new BusinessRuleException("Referenced financial data must belong to the same owner.");
    }

    private IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> FinanceEntries() => ChangeTracker.Entries().Where(entry => IsFinanceEntity(entry.Entity) && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    private static bool IsFinanceEntity(object entity) => entity is PlannerMonthRecord or PlannerItem or Account or Category or Transaction or RecurringTransaction or RecurringTransactionOccurrence or Debt;
    private static Guid GetOwnerId(object entity) => entity switch { PlannerMonthRecord value => value.OwnerId, PlannerItem value => value.OwnerId, Account value => value.OwnerId, Category value => value.OwnerId, Transaction value => value.OwnerId, RecurringTransaction value => value.OwnerId, RecurringTransactionOccurrence value => value.OwnerId, Debt value => value.OwnerId, _ => throw new InvalidOperationException() };
    private static Guid GetEntityId(object entity) => entity switch { PlannerMonthRecord value => value.Id, PlannerItem value => value.Id, Account value => value.Id, Category value => value.Id, Transaction value => value.Id, RecurringTransaction value => value.Id, RecurringTransactionOccurrence value => value.Id, Debt value => value.Id, _ => Guid.Empty };
    private static void SetOwnerId(object entity, Guid ownerId)
    {
        switch (entity) { case PlannerMonthRecord value: value.OwnerId = ownerId; break; case PlannerItem value: value.OwnerId = ownerId; break; case Account value: value.OwnerId = ownerId; break; case Category value: value.OwnerId = ownerId; break; case Transaction value: value.OwnerId = ownerId; break; case RecurringTransaction value: value.OwnerId = ownerId; break; case RecurringTransactionOccurrence value: value.OwnerId = ownerId; break; case Debt value: value.OwnerId = ownerId; break; }
    }

    private static IEnumerable<(Type Type, Guid Id)> GetReferences(object entity) => entity switch
    {
        PlannerItem value => OptionalReferences((typeof(Account), value.AccountId), (typeof(Account), value.TargetAccountId), (typeof(Category), value.CategoryId)),
        Category { ParentCategoryId: { } id } => [(typeof(Category), id)],
        Debt { AccountId: { } id } => [(typeof(Account), id)],
        Transaction value => OptionalReferences((typeof(Account), value.AccountId), (typeof(Account), value.TargetAccountId), (typeof(Category), value.CategoryId), (typeof(Debt), value.DebtId)),
        RecurringTransaction value => OptionalReferences((typeof(Account), value.AccountId), (typeof(Category), value.CategoryId), (typeof(Debt), value.DebtId)),
        RecurringTransactionOccurrence value => OptionalReferences((typeof(RecurringTransaction), value.RecurringTransactionId), (typeof(Transaction), value.TransactionId)),
        _ => []
    };
    private static IEnumerable<(Type Type, Guid Id)> OptionalReferences(params (Type Type, Guid? Id)[] references) => references.Where(reference => reference.Id is not null).Select(reference => (reference.Type, reference.Id!.Value));

    private bool ReferenceHasOwner(Type type, Guid id, Guid ownerId)
    {
        var tracked = FinanceEntries().FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.GetType() == type && GetEntityId(entry.Entity) == id);
        if (tracked is not null) return GetOwnerId(tracked.Entity) == ownerId;
        if (type == typeof(Account)) return Accounts.IgnoreQueryFilters().Any(entity => entity.Id == id && entity.OwnerId == ownerId);
        if (type == typeof(Category)) return Categories.IgnoreQueryFilters().Any(entity => entity.Id == id && entity.OwnerId == ownerId);
        if (type == typeof(Debt)) return Debts.IgnoreQueryFilters().Any(entity => entity.Id == id && entity.OwnerId == ownerId);
        if (type == typeof(Transaction)) return Transactions.IgnoreQueryFilters().Any(entity => entity.Id == id && entity.OwnerId == ownerId);
        return RecurringTransactions.IgnoreQueryFilters().Any(entity => entity.Id == id && entity.OwnerId == ownerId);
    }

    private bool PersistedOwnerMatches(object entity, Guid ownerId) => entity switch
    {
        PlannerMonthRecord value => PlannerMonths.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        PlannerItem value => PlannerItems.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        Account value => Accounts.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        Category value => Categories.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        Transaction value => Transactions.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        RecurringTransaction value => RecurringTransactions.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        RecurringTransactionOccurrence value => RecurringTransactionOccurrences.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        Debt value => Debts.IgnoreQueryFilters().AsNoTracking().Any(item => item.Id == value.Id && item.OwnerId == ownerId),
        _ => false
    };

    private Task<bool> PersistedOwnerMatchesAsync(object entity, Guid ownerId, CancellationToken cancellationToken) => entity switch
    {
        PlannerMonthRecord value => PlannerMonths.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        PlannerItem value => PlannerItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        Account value => Accounts.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        Category value => Categories.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        Transaction value => Transactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        RecurringTransaction value => RecurringTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        RecurringTransactionOccurrence value => RecurringTransactionOccurrences.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        Debt value => Debts.IgnoreQueryFilters().AsNoTracking().AnyAsync(item => item.Id == value.Id && item.OwnerId == ownerId, cancellationToken),
        _ => Task.FromResult(false)
    };

    private async Task<bool> ReferenceHasOwnerAsync(Type type, Guid id, Guid ownerId, CancellationToken cancellationToken)
    {
        var tracked = FinanceEntries().FirstOrDefault(entry => entry.State != EntityState.Deleted && entry.Entity.GetType() == type && GetEntityId(entry.Entity) == id);
        if (tracked is not null) return GetOwnerId(tracked.Entity) == ownerId;
        if (type == typeof(Account)) return await Accounts.IgnoreQueryFilters().AnyAsync(entity => entity.Id == id && entity.OwnerId == ownerId, cancellationToken);
        if (type == typeof(Category)) return await Categories.IgnoreQueryFilters().AnyAsync(entity => entity.Id == id && entity.OwnerId == ownerId, cancellationToken);
        if (type == typeof(Debt)) return await Debts.IgnoreQueryFilters().AnyAsync(entity => entity.Id == id && entity.OwnerId == ownerId, cancellationToken);
        if (type == typeof(Transaction)) return await Transactions.IgnoreQueryFilters().AnyAsync(entity => entity.Id == id && entity.OwnerId == ownerId, cancellationToken);
        return await RecurringTransactions.IgnoreQueryFilters().AnyAsync(entity => entity.Id == id && entity.OwnerId == ownerId, cancellationToken);
    }
}
