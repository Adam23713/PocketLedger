using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;

namespace PocketLedger.Services;

public interface ICrowdSecBanManager
{
    bool IsConfigured { get; }
    Task<bool> BanAsync(string ipAddress, TimeSpan duration, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(string addressOrRange, CancellationToken cancellationToken = default);
}

// Bouncer API keys can only read decisions, so writes use a dedicated CrowdSec machine account. Caddy's bouncer picks changes up within its five-second polling interval.
public sealed class CrowdSecBanManager(HttpClient httpClient, IOptions<CrowdSecOptions> options, TimeProvider timeProvider, ILogger<CrowdSecBanManager> logger) : ICrowdSecBanManager
{
    internal const string Scenario = "pocketledger/admin-ban";
    internal const string Origin = "pocketledger-admin";
    // LAPI rejects a machine login unless the user agent has the form name/version.
    private const string UserAgent = "pocketledger-identity/1.0";

    public bool IsConfigured
    {
        get
        {
            var settings = options.Value;
            return !string.IsNullOrWhiteSpace(settings.BaseUrl) && !string.IsNullOrWhiteSpace(settings.MachineId) && !string.IsNullOrWhiteSpace(settings.MachinePassword);
        }
    }

    public Task<bool> BanAsync(string ipAddress, TimeSpan duration, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
    {
        var now = timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var alert = new
        {
            scenario = Scenario, scenario_hash = "", scenario_version = "", message = "Banned from the PocketLedger admin dashboard", events_count = 1, start_at = now, stop_at = now,
            capacity = 0, leakspeed = "0", simulated = false, remediation = true, events = Array.Empty<object>(),
            source = new { scope = "Ip", value = ipAddress, ip = ipAddress },
            decisions = new[] { new { origin = Origin, type = "ban", scope = "Ip", value = ipAddress, duration = string.Create(CultureInfo.InvariantCulture, $"{(long)duration.TotalHours}h"), scenario = Scenario } }
        };
        return new HttpRequestMessage(HttpMethod.Post, Endpoint("v1/alerts")) { Content = JsonContent.Create(new[] { alert }) };
    }, "ban", cancellationToken);

    // Matches the exact stored value, so removing an address leaves a separate range ban that contains it in place.
    public Task<bool> RemoveAsync(string addressOrRange, CancellationToken cancellationToken = default) => ExecuteAsync(() =>
    {
        var scope = addressOrRange.Contains('/') ? "Range" : "Ip";
        return new HttpRequestMessage(HttpMethod.Delete, Endpoint($"v1/decisions?scopes={scope}&value={Uri.EscapeDataString(addressOrRange)}"));
    }, "removal", cancellationToken);

    private async Task<bool> ExecuteAsync(Func<HttpRequestMessage> createRequest, string operation, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return false;
        try
        {
            var token = await LogInAsync(cancellationToken);
            using var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The CrowdSec {Operation} request failed.", operation);
            return false;
        }
    }

    private async Task<string> LogInAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("v1/watchers/login"))
        {
            Content = JsonContent.Create(new { machine_id = settings.MachineId, password = settings.MachinePassword, scenarios = Array.Empty<string>() })
        };
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
        return login?.Token is { Length: > 0 } token ? token : throw new HttpRequestException("CrowdSec returned no machine token.", null, HttpStatusCode.BadGateway);
    }

    private Uri Endpoint(string path) => new(new Uri(options.Value.BaseUrl!, UriKind.Absolute), path);

    private sealed class LoginResponse
    {
        [JsonPropertyName("token")] public string? Token { get; init; }
    }
}
