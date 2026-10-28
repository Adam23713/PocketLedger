using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PocketLedger.Landing.Contact;

public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken = default);
}

// Cloudflare Turnstile proves a browser solved the challenge; tokens are single use and only valid for the action rendered on the form.
public sealed class TurnstileVerifier(HttpClient client, IOptions<ContactOptions> options, ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    public const string Action = "contact";
    private const int MaximumTokenLength = 2048;

    public async Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken = default)
    {
        var secret = options.Value.Turnstile.SecretKey;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(token) || token.Length > MaximumTokenLength) return false;

        var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (!string.IsNullOrEmpty(clientIp)) form["remoteip"] = clientIp;
        try
        {
            using var response = await client.PostAsync("turnstile/v0/siteverify", new FormUrlEncodedContent(form), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile verification returned HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }
            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            // Cloudflare's test keys answer without an action, so only a different action is treated as a mismatch.
            if (result is { Success: true } && (string.IsNullOrEmpty(result.Action) || result.Action == Action)) return true;
            logger.LogInformation("Turnstile rejected a contact form token: {ErrorCodes}.", string.Join(", ", result?.ErrorCodes ?? []));
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Fail closed: without a verdict from Cloudflare the submission cannot be told apart from a bot.
            logger.LogWarning(exception, "Turnstile verification could not be completed.");
            return false;
        }
    }

    private sealed record SiteVerifyResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("action")] string? Action,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}
