namespace PocketLedger.Services;

public interface ICurrentUser
{
    Guid UserId { get; }
    bool IsAuthenticated { get; }
}
