using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PocketLedger.Contracts;
using PocketLedger.Services;

namespace PocketLedger.Web.Api;

public abstract class ApiClientBase(HttpClient httpClient)
{
    protected HttpClient HttpClient { get; } = httpClient;
    protected static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    protected async Task<T> GetAsync<T>(string uri, CancellationToken token)
    {
        using var response = await HttpClient.GetAsync(uri, token);
        await EnsureSuccessAsync(response, token);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, token))!;
    }

    protected async Task<T?> GetOrDefaultAsync<T>(string uri, CancellationToken token)
    {
        using var response = await HttpClient.GetAsync(uri, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        await EnsureSuccessAsync(response, token);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, token);
    }

    protected async Task<TResponse> PostAsync<TRequest, TResponse>(string uri, TRequest request, CancellationToken token)
    {
        using var response = await HttpClient.PostAsJsonAsync(uri, request, JsonOptions, token);
        await EnsureSuccessAsync(response, token);
        return (await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions, token))!;
    }

    protected async Task PostAsync<TRequest>(string uri, TRequest request, CancellationToken token)
    {
        using var response = await HttpClient.PostAsJsonAsync(uri, request, JsonOptions, token);
        await EnsureSuccessAsync(response, token);
    }

    protected async Task PutAsync<TRequest>(string uri, TRequest request, CancellationToken token)
    {
        using var response = await HttpClient.PutAsJsonAsync(uri, request, JsonOptions, token);
        await EnsureSuccessAsync(response, token);
    }

    protected async Task DeleteAsync(string uri, CancellationToken token)
    {
        using var response = await HttpClient.DeleteAsync(uri, token);
        await EnsureSuccessAsync(response, token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, token);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new EntityNotFoundException(error?.Message ?? "The requested resource was not found.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new BusinessRuleException(error?.Message ?? "The request violates a business rule.");
        throw new HttpRequestException(error?.Message ?? $"API request failed with status {(int)response.StatusCode}.", null, response.StatusCode);
    }

    protected static string Query(string path, IEnumerable<KeyValuePair<string, string?>> values)
    {
        var filtered = values.Where(item => item.Value is not null).Select(item => new KeyValuePair<string, string?>(item.Key, item.Value));
        return path + QueryString.Create(filtered);
    }
}
