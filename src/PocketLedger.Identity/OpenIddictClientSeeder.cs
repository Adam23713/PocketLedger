using OpenIddict.Abstractions;

namespace PocketLedger;

public sealed class OpenIddictClientSeeder(IServiceProvider services, IConfiguration configuration, IWebHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var webClient = await manager.FindByClientIdAsync("pocketledger-web", cancellationToken);
        if (webClient is null)
        {
            var clientSecret = configuration["OpenIddict:WebClientSecret"] ?? throw new InvalidOperationException("OpenIddict:WebClientSecret is required.");
            var webBaseUrl = configuration["OpenIddict:WebBaseUrl"] ?? "https://app.localhost";
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "pocketledger-web",
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
        else
        {
            var webBaseUrl = (configuration["OpenIddict:WebBaseUrl"] ?? "https://app.localhost").TrimEnd('/');
            var redirectUri = new Uri($"{webBaseUrl}/signin-oidc");
            var logoutUri = new Uri($"{webBaseUrl}/signout-callback-oidc");
            var descriptor = new OpenIddictApplicationDescriptor();
            await manager.PopulateAsync(descriptor, webClient, cancellationToken);
            if (!descriptor.RedirectUris.SetEquals([redirectUri]) || !descriptor.PostLogoutRedirectUris.SetEquals([logoutUri]))
            {
                descriptor.RedirectUris.Clear();
                descriptor.RedirectUris.Add(redirectUri);
                descriptor.PostLogoutRedirectUris.Clear();
                descriptor.PostLogoutRedirectUris.Add(logoutUri);
                await manager.UpdateAsync(webClient, descriptor, cancellationToken);
            }
        }

        if (environment.IsDevelopment() && await manager.FindByClientIdAsync("pocketledger-swagger", cancellationToken) is null)
        {
            var swaggerBaseUrl = configuration["OpenIddict:SwaggerBaseUrl"] ?? "http://localhost:5051";
            await manager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "pocketledger-swagger",
                ClientType = OpenIddictConstants.ClientTypes.Public,
                ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
                DisplayName = "PocketLedger Swagger",
                RedirectUris = { new Uri($"{swaggerBaseUrl.TrimEnd('/')}/swagger/oauth2-redirect.html") },
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Authorization,
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                    OpenIddictConstants.Permissions.ResponseTypes.Code,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
                    OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "pocketledger.api"
                },
                Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
            }, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
