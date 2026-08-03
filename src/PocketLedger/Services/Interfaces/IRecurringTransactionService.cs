using PocketLedger.Models.Entities;

namespace PocketLedger.Services.Interfaces;

public interface IRecurringTransactionService
{
    Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken cancellationToken);
    Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<RecurringTransaction> CreateAsync(RecurringTransaction template, CancellationToken cancellationToken);
    Task UpdateAsync(RecurringTransaction template, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
