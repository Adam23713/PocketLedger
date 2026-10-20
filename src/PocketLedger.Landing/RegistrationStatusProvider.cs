using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PocketLedger.Landing;

public sealed record RegistrationStatus(bool AcceptsRegistrations, int? MaximumUserCount, int? RemainingSlots)
{
    public static readonly RegistrationStatus Closed = new(false, null, null);
}

public interface IRegistrationStatusProvider
{
    Task<RegistrationStatus> GetAsync(CancellationToken cancellationToken = default);
}

// Identity owns the registration settings; the landing page only caches them briefly so the banner stays cheap to render.
public sealed class RegistrationStatusProvider(HttpClient client, IMemoryCache cache, ILogger<RegistrationStatusProvider> logger) : IRegistrationStatusProvider
{
    private static readonly TimeSpan SuccessLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureLifetime = TimeSpan.FromSeconds(10);
    private const string CacheKey = "registration-status";

    public async Task<RegistrationStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out RegistrationStatus? cached) && cached is not null) return cached;
        RegistrationStatus status;
        TimeSpan lifetime;
        try
        {
            var response = await client.GetFromJsonAsync<StatusResponse>("Registration/Status", cancellationToken)
                ?? throw new InvalidOperationException("Identity returned an empty registration status.");
            status = new RegistrationStatus(response.AcceptsRegistrations, response.MaximumUserCount, response.RemainingSlots);
            lifetime = SuccessLifetime;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Registration is impossible while Identity is unreachable, so the closed banner is the honest fallback.
            logger.LogWarning(exception, "The registration status could not be read from Identity.");
            status = RegistrationStatus.Closed;
            lifetime = FailureLifetime;
        }
        cache.Set(CacheKey, status, lifetime);
        return status;
    }

    private sealed record StatusResponse(bool AcceptsRegistrations, int? MaximumUserCount, int? RemainingSlots);
}
