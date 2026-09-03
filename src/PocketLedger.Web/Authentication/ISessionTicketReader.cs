using Microsoft.AspNetCore.Authentication;

namespace PocketLedger.Web.Authentication;

public interface ISessionTicketReader
{
    Task<AuthenticationTicket?> RetrieveAsync(string key, CancellationToken cancellationToken);
}
