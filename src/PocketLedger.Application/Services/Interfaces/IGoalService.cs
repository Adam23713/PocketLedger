using PocketLedger.Models.Entities;

namespace PocketLedger.Services.Interfaces;

public interface IGoalService
{
    Task<IReadOnlyList<Goal>> GetAllAsync(CancellationToken cancellationToken);
    Task<Goal?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Goal> CreateAsync(Goal goal, CancellationToken cancellationToken);
    Task UpdateAsync(Goal goal, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<Goal> AddContributionAsync(Guid id, decimal amount, CancellationToken cancellationToken);
}
