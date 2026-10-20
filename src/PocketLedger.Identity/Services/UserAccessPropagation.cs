using System.Net.Http.Json;
using PocketLedger.Security;

namespace PocketLedger.Services;

/// <summary>
/// Pushes Identity's access-block decisions to the services that cannot read the Identity database:
/// the API (rejects the user's access tokens) and the web app (terminates the user's sessions).
/// </summary>
public interface IUserAccessPropagation
{
    Task BlockAsync(Guid userId, CancellationToken cancellationToken = default);
    Task UnblockAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SynchronizeAsync(IReadOnlyCollection<Guid> blockedUserIds, CancellationToken cancellationToken = default);
}

public sealed class UserAccessPropagation(IHttpClientFactory clients, IConfiguration configuration) : IUserAccessPropagation
{
    public const string ApiClientName = "PocketLedger.Api.Internal";
    public const string WebClientName = "PocketLedger.Web.Internal";

    public async Task BlockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await SendAsync(ApiClientName, HttpMethod.Put, $"api/v1/internal-admin/users/{userId}/access-block", null, cancellationToken);
        await SendAsync(WebClientName, HttpMethod.Put, $"internal-admin/users/{userId}/sessions/revoke", null, cancellationToken);
    }

    public Task UnblockAsync(Guid userId, CancellationToken cancellationToken = default)
        => SendAsync(ApiClientName, HttpMethod.Delete, $"api/v1/internal-admin/users/{userId}/access-block", null, cancellationToken);

    public async Task SynchronizeAsync(IReadOnlyCollection<Guid> blockedUserIds, CancellationToken cancellationToken = default)
    {
        await SendAsync(ApiClientName, HttpMethod.Put, "api/v1/internal-admin/users/access-blocks", blockedUserIds, cancellationToken);
        await SendAsync(WebClientName, HttpMethod.Put, "internal-admin/users/sessions/revoke", blockedUserIds, cancellationToken);
    }

    private async Task SendAsync(string clientName, HttpMethod method, string path, IReadOnlyCollection<Guid>? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        InternalAdminAuthentication.AddCredential(request, configuration);
        using var response = await clients.CreateClient(clientName).SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
