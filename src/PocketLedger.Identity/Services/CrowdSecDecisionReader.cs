using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;

namespace PocketLedger.Services;

public sealed record CrowdSecBanDecision(string Address, string Scope, string Scenario, string Origin, string Action, string? RemainingDuration);

public sealed record CrowdSecBanDecisionsResult(bool IsAvailable, IReadOnlyList<CrowdSecBanDecision> Decisions);

public interface ICrowdSecDecisionReader
{
    Task<CrowdSecBanDecisionsResult> GetActiveBansAsync(CancellationToken cancellationToken = default);
}

public sealed class CrowdSecDecisionReader(HttpClient httpClient, IOptions<CrowdSecOptions> options, ILogger<CrowdSecDecisionReader> logger) : ICrowdSecDecisionReader
{
    public async Task<CrowdSecBanDecisionsResult> GetActiveBansAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BaseUrl) || string.IsNullOrWhiteSpace(settings.ApiKey)) return new CrowdSecBanDecisionsResult(false, []);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(settings.BaseUrl, UriKind.Absolute), "v1/decisions?type=ban"));
            request.Headers.Add("X-Api-Key", settings.ApiKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var decisions = await response.Content.ReadFromJsonAsync<CrowdSecDecisionDto[]>(cancellationToken: cancellationToken) ?? [];
            var activeBans = decisions.Where(item => string.Equals(item.Type, "ban", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(item.Scope, "ip", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Scope, "range", StringComparison.OrdinalIgnoreCase)))
                .Select(item => new CrowdSecBanDecision(item.Value ?? "Unavailable", item.Scope ?? "Unavailable", item.Scenario ?? "Not specified", item.Origin ?? "Not specified",
                    item.Type ?? "ban", string.IsNullOrWhiteSpace(item.Duration) ? null : item.Duration)).OrderBy(item => item.Address, StringComparer.OrdinalIgnoreCase).ToArray();
            return new CrowdSecBanDecisionsResult(true, activeBans);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "CrowdSec decisions are unavailable.");
            return new CrowdSecBanDecisionsResult(false, []);
        }
    }

    private sealed class CrowdSecDecisionDto
    {
        [JsonPropertyName("value")] public string? Value { get; init; }
        [JsonPropertyName("scope")] public string? Scope { get; init; }
        [JsonPropertyName("scenario")] public string? Scenario { get; init; }
        [JsonPropertyName("origin")] public string? Origin { get; init; }
        [JsonPropertyName("type")] public string? Type { get; init; }
        [JsonPropertyName("duration")] public string? Duration { get; init; }
    }
}
