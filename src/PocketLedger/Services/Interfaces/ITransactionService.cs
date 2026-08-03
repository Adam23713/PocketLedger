using PocketLedger.Models.Entities;

namespace PocketLedger.Services.Interfaces;

public interface ITransactionService
{
    Task<IReadOnlyList<Transaction>> GetForMonthAsync(int year, int month, CancellationToken cancellationToken);
    Task<IReadOnlyList<Transaction>> GetRecentAsync(int count, CancellationToken cancellationToken);
    Task<PagedResult<Transaction>> GetFilteredAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<TransactionDailyTotal>> GetDailyTotalsAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyList<Transaction>> GetForExportAsync(TransactionFilter filter, CancellationToken cancellationToken);
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Transaction> CreateAsync(Transaction transaction, CancellationToken cancellationToken);
    Task UpdateAsync(Transaction transaction, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, decimal>> CalculateAccountBalancesAsync(CancellationToken cancellationToken);
    Task<decimal> CalculateMainBalanceAsync(CancellationToken cancellationToken);
}
