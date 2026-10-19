namespace PocketLedger.Services.Interfaces;

public interface IUserDataDeletionService
{
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}
