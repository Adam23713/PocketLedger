using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;

namespace PocketLedger.Identity.Tests;

public sealed class FacebookAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public FacebookAuthenticationTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Theory]
    [InlineData(null, null)]
    [InlineData("facebook-app-id", null)]
    [InlineData(null, "facebook-app-secret")]
    public async Task IncompleteConfiguration_DoesNotRegisterFacebookScheme(string? appId, string? appSecret)
    {
        using var configuredFactory = ConfigureFactory(appId, appSecret);

        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(FacebookDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task CompleteConfiguration_RegistersMinimalFacebookSchemeAndMapsStableSubjectAndVerifiedEmail()
    {
        using var configuredFactory = ConfigureFactory("facebook-app-id", "facebook-app-secret");
        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<FacebookOptions>>().Get(FacebookDefaults.AuthenticationScheme);

        var scheme = await schemes.GetSchemeAsync(FacebookDefaults.AuthenticationScheme);
        using var userData = JsonDocument.Parse("""{"id":"facebook-subject","email":" user@example.test "}""");
        var identity = new ClaimsIdentity();
        foreach (var action in options.ClaimActions) action.Run(userData.RootElement, identity, FacebookDefaults.AuthenticationScheme);

        Assert.Equal("Facebook", scheme?.DisplayName);
        Assert.Equal("facebook-app-id", options.AppId);
        Assert.Equal("facebook-app-secret", options.AppSecret);
        Assert.Equal(["email"], options.Scope);
        Assert.Equal(["id", "email"], options.Fields);
        Assert.Equal("/signin-facebook", options.CallbackPath);
        Assert.Equal("facebook-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@example.test", identity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(bool.TrueString, identity.FindFirst("email_verified")?.Value, ignoreCase: true);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":null}")]
    [InlineData("{\"email\":\"not-an-email\"}")]
    public void MissingOrInvalidEmail_DoesNotCreateEmailOrVerifiedClaims(string json)
    {
        using var configuredFactory = ConfigureFactory("facebook-app-id", "facebook-app-secret");
        var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<FacebookOptions>>().Get(FacebookDefaults.AuthenticationScheme);
        using var userData = JsonDocument.Parse(json);
        var identity = new ClaimsIdentity();

        foreach (var action in options.ClaimActions) action.Run(userData.RootElement, identity, FacebookDefaults.AuthenticationScheme);

        Assert.Null(identity.FindFirst(ClaimTypes.Email));
        Assert.Null(identity.FindFirst("email_verified"));
    }

    private WebApplicationFactory<Program> ConfigureFactory(string? appId, string? appSecret)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:Facebook:AppId", appId ?? string.Empty);
            builder.UseSetting("Authentication:Facebook:AppSecret", appSecret ?? string.Empty);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase($"{nameof(FacebookAuthenticationTests)}-{appId}-{appSecret}"); options.UseOpenIddict(); });
            });
        });
}
