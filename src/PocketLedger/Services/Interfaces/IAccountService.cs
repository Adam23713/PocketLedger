using PocketLedger.Models.Entities;

namespace PocketLedger.Services.Interfaces;

public record AccountChoice(Guid Id, string Name, string Currency);
public record AccountDeletionSummary(int TransactionCount, int RecurringTransactionCount, int DebtCount);

public interface IAccountService
{
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken);
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Account> CreateAsync(Account account, CancellationToken cancellationToken);
    Task UpdateAsync(Account account, bool createInitialBalanceAdjustment, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<AccountDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<decimal> GetCurrentBalanceAsync(Guid accountId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, decimal>> GetCurrentBalancesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountChoice>> GetChoicesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Transaction>> GetRecentTransactionsAsync(Guid accountId, int count, CancellationToken cancellationToken);
}
