using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth;

namespace PocketLedger.Services;

public static class GitHubAuthenticationEvents
{
    public const string SchemeName = "GitHub";
    private const string EmailsEndpoint = "https://api.github.com/user/emails";

    public static async Task CreatingTicketAsync(OAuthCreatingTicketContext context)
    {
        using var user = await GetJsonAsync(context, context.Options.UserInformationEndpoint);
        if (user is null) return;
        context.RunClaimActions(user.RootElement);

        // The profile email can be hidden and carries no verification flag, so only the emails endpoint is trusted.
        using var emails = await GetJsonAsync(context, EmailsEndpoint);
        if (emails is null || SelectVerifiedPrimaryEmail(emails.RootElement) is not { } email) return;
        var issuer = context.Options.ClaimsIssuer ?? context.Scheme.Name;
        context.Identity?.AddClaim(new Claim(ClaimTypes.Email, email, ClaimValueTypes.String, issuer));
        context.Identity?.AddClaim(new Claim("email_verified", bool.TrueString, ClaimValueTypes.Boolean, issuer));
    }

    public static string? SelectVerifiedPrimaryEmail(JsonElement emails)
    {
        if (emails.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in emails.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("primary", out var primary) || primary.ValueKind != JsonValueKind.True) continue;
            if (!item.TryGetProperty("verified", out var verified) || verified.ValueKind != JsonValueKind.True) continue;
            if (!item.TryGetProperty("email", out var emailElement) || emailElement.ValueKind != JsonValueKind.String) continue;
            var email = emailElement.GetString()?.Trim();
            return !string.IsNullOrEmpty(email) && new EmailAddressAttribute().IsValid(email) ? email : null;
        }
        return null;
    }

    // A failed request leaves the claims out, so the account controller rejects the sign-in and audits the reason.
    private static async Task<JsonDocument?> GetJsonAsync(OAuthCreatingTicketContext context, string endpoint)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(GitHubAuthenticationEvents)).LogWarning("GitHub sign-in request to {Endpoint} failed with status {StatusCode}.", endpoint, (int)response.StatusCode);
            return null;
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
    }
}
