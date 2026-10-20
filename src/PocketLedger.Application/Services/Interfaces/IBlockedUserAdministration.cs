namespace PocketLedger.Services.Interfaces;

public interface IBlockedUserAdministration
{
    Task BlockAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UnblockAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SynchronizeAsync(IReadOnlyCollection<Guid> blockedUserIds, CancellationToken cancellationToken = default);
}
