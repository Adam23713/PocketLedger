using PocketLedger.Security;

namespace PocketLedger.Services;

public interface IAdminUserDataClient
{
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class AdminUserDataClient(HttpClient client, IConfiguration configuration) : IAdminUserDataClient
{
    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/v1/internal-admin/users/{userId}");
        InternalAdminAuthentication.AddCredential(request, configuration);
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
