using OpenIddict.Abstractions;

namespace PocketLedger;

public sealed class OpenIddictClientSeeder(IServiceProvider services, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        const string clientId = "pocketledger-web";
        if (await manager.FindByClientIdAsync(clientId, cancellationToken) is not null) return;
        var clientSecret = configuration["OpenIddict:WebClientSecret"] ?? throw new InvalidOperationException("OpenIddict:WebClientSecret is required.");
        var webBaseUrl = configuration["OpenIddict:WebBaseUrl"] ?? "https://app.localhost";
        await manager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = "PocketLedger Web",
            RedirectUris = { new Uri($"{webBaseUrl.TrimEnd('/')}/signin-oidc") },
            PostLogoutRedirectUris = { new Uri($"{webBaseUrl.TrimEnd('/')}/signout-callback-oidc") },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
                OpenIddictConstants.Permissions.Prefixes.Scope + "pocketledger.api"
            },
            Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
