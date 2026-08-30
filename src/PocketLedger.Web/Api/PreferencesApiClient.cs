using PocketLedger.Contracts;

namespace PocketLedger.Web.Api;

public interface IPreferencesApiClient
{
    Task<UserPreferenceResponse> GetAsync(CancellationToken token);
    Task UpdateAsync(UserPreferenceUpdateRequest request, CancellationToken token);
}

public sealed class PreferencesApiClient(HttpClient client) : ApiClientBase(client), IPreferencesApiClient
{
    public Task<UserPreferenceResponse> GetAsync(CancellationToken token) => GetAsync<UserPreferenceResponse>("api/v1/preferences", token);
    public Task UpdateAsync(UserPreferenceUpdateRequest request, CancellationToken token) => PutAsync("api/v1/preferences", request, token);
}
