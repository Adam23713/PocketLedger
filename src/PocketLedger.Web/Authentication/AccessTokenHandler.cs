using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace PocketLedger.Web.Authentication;

public sealed class AccessTokenHandler(IHttpContextAccessor contextAccessor, IHttpClientFactory clients, IConfiguration configuration) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = contextAccessor.HttpContext ?? throw new InvalidOperationException("An active HTTP request is required.");
        var authentication = await context.AuthenticateAsync("BffCookie");
        if (!authentication.Succeeded || authentication.Principal is null) throw new InvalidOperationException("An authenticated BFF session is required.");
        var properties = authentication.Properties ?? throw new InvalidOperationException("The BFF session has no authentication properties.");
        var accessToken = properties.GetTokenValue("access_token");
        var expiresAt = properties.GetTokenValue("expires_at");
        if (string.IsNullOrWhiteSpace(accessToken) || !DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiration) || expiration <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            accessToken = await RefreshAsync(context, authentication, cancellationToken);
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> RefreshAsync(HttpContext context, AuthenticateResult authentication, CancellationToken cancellationToken)
    {
        var properties = authentication.Properties ?? throw new InvalidOperationException("The BFF session has no authentication properties.");
        var refreshToken = properties.GetTokenValue("refresh_token") ?? throw new InvalidOperationException("The BFF session has no refresh token.");
        var client = clients.CreateClient("IdentityToken");
        using var response = await client.PostAsync(configuration["Identity:TokenEndpoint"] ?? "connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = configuration["Identity:ClientId"] ?? "pocketledger-web",
            ["client_secret"] = configuration["Identity:ClientSecret"] ?? throw new InvalidOperationException("Identity:ClientSecret is required.")
        }), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var root = document.RootElement;
        var accessToken = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();
        var tokens = properties.GetTokens().Where(token => token.Name is not "access_token" and not "refresh_token" and not "expires_at").ToList();
        tokens.Add(new AuthenticationToken { Name = "access_token", Value = accessToken });
        tokens.Add(new AuthenticationToken { Name = "refresh_token", Value = root.TryGetProperty("refresh_token", out var refreshed) ? refreshed.GetString() ?? refreshToken : refreshToken });
        tokens.Add(new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToString("o", CultureInfo.InvariantCulture) });
        properties.StoreTokens(tokens);
        await context.SignInAsync("BffCookie", authentication.Principal!, properties);
        return accessToken;
    }
}
