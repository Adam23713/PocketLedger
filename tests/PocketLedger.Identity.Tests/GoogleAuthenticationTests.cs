using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;

namespace PocketLedger.Identity.Tests;

public sealed class GoogleAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public GoogleAuthenticationTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Theory]
    [InlineData(null, null)]
    [InlineData("google-client-id", null)]
    [InlineData(null, "google-client-secret")]
    public async Task IncompleteConfiguration_DoesNotRegisterGoogleScheme(string? clientId, string? clientSecret)
    {
        using var configuredFactory = ConfigureFactory(clientId, clientSecret);

        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme));
    }

    [Fact]
    public async Task CompleteConfiguration_RegistersMinimalGoogleSchemeAndMapsStableSubjectAndVerifiedEmail()
    {
        using var configuredFactory = ConfigureFactory("google-client-id", "google-client-secret");
        var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);

        var scheme = await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme);
        using var userData = JsonDocument.Parse("""{"sub":"google-subject","email":"user@example.test","email_verified":true}""");
        var identity = new ClaimsIdentity();
        foreach (var action in options.ClaimActions) action.Run(userData.RootElement, identity, GoogleDefaults.AuthenticationScheme);

        Assert.Equal("Google", scheme?.DisplayName);
        Assert.Equal("google-client-id", options.ClientId);
        Assert.Equal("google-client-secret", options.ClientSecret);
        Assert.Equal(["openid", "email"], options.Scope);
        Assert.Equal("google-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@example.test", identity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal(bool.TrueString, identity.FindFirst("email_verified")?.Value, ignoreCase: true);
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void UnverifiedOrMissingVerification_DoesNotProduceVerifiedEmailClaim(string? verifiedEmail)
    {
        using var configuredFactory = ConfigureFactory("google-client-id", "google-client-secret");
        var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(GoogleDefaults.AuthenticationScheme);
        var verification = verifiedEmail is null ? string.Empty : $",\"email_verified\":{verifiedEmail}";
        using var userData = JsonDocument.Parse($"{{\"sub\":\"google-subject\",\"email\":\"user@example.test\"{verification}}}");
        var identity = new ClaimsIdentity();

        foreach (var action in options.ClaimActions) action.Run(userData.RootElement, identity, GoogleDefaults.AuthenticationScheme);

        Assert.False(string.Equals(bool.TrueString, identity.FindFirst("email_verified")?.Value, StringComparison.OrdinalIgnoreCase));
    }

    private WebApplicationFactory<Program> ConfigureFactory(string? clientId, string? clientSecret)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:Google:ClientId", clientId ?? string.Empty);
            builder.UseSetting("Authentication:Google:ClientSecret", clientSecret ?? string.Empty);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase($"{nameof(GoogleAuthenticationTests)}-{clientId}-{clientSecret}"); options.UseOpenIddict(); });
            });
        });
}
