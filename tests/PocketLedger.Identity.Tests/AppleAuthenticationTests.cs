using System.Security.Claims;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class AppleAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string PrivateKey = "-----BEGIN PRIVATE KEY-----\ntest-private-key\n-----END PRIVATE KEY-----";
    private readonly WebApplicationFactory<Program> factory;

    public AppleAuthenticationTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task IncompleteConfiguration_DoesNotRegisterAppleScheme()
    {
        var privateKeyPath = await CreatePrivateKeyFileAsync();
        try
        {
            var configurations = new[]
            {
                new AppleTestConfiguration(null, null, null, null),
                new AppleTestConfiguration(null, "team-id", "key-id", privateKeyPath),
                new AppleTestConfiguration("client-id", null, "key-id", privateKeyPath),
                new AppleTestConfiguration("client-id", "team-id", null, privateKeyPath),
                new AppleTestConfiguration("client-id", "team-id", "key-id", null),
                new AppleTestConfiguration("client-id", "team-id", "key-id", Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.p8"))
            };

            foreach (var configuration in configurations)
            {
                using var configuredFactory = ConfigureFactory(configuration);
                var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
                Assert.Null(await schemes.GetSchemeAsync(AppleAuthenticationDefaults.AuthenticationScheme));
            }
        }
        finally
        {
            File.Delete(privateKeyPath);
        }
    }

    [Fact]
    public async Task CompleteConfiguration_RegistersMinimalValidatedSchemeAndReadsPrivateKeyAsynchronously()
    {
        var privateKeyPath = await CreatePrivateKeyFileAsync();
        try
        {
            using var configuredFactory = ConfigureFactory(new AppleTestConfiguration("client-id", "team-id", "key-id", privateKeyPath));
            var schemes = configuredFactory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
            var options = configuredFactory.Services.GetRequiredService<IOptionsMonitor<AppleAuthenticationOptions>>().Get(AppleAuthenticationDefaults.AuthenticationScheme);

            var scheme = await schemes.GetSchemeAsync(AppleAuthenticationDefaults.AuthenticationScheme);
            var loadedPrivateKey = await options.PrivateKey!(options.KeyId!, CancellationToken.None);

            Assert.Equal("Apple", scheme?.DisplayName);
            Assert.Equal("client-id", options.ClientId);
            Assert.Equal("team-id", options.TeamId);
            Assert.Equal("key-id", options.KeyId);
            Assert.True(options.GenerateClientSecret);
            Assert.True(options.ValidateTokens);
            Assert.Equal("/signin-apple", options.CallbackPath);
            Assert.Equal(["openid", "email"], options.Scope);
            Assert.Equal(PrivateKey, loadedPrivateKey.ToString());
        }
        finally
        {
            File.Delete(privateKeyPath);
        }
    }

    [Fact]
    public void ClaimNormalization_PreservesStableSubjectAndCanonicalizesVerifiedEmail()
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "apple-stable-subject"),
            new Claim(ClaimTypes.Email, " user@privaterelay.appleid.com "),
            new Claim("email_verified", "TRUE")
        ]);

        AppleIdentityClaims.Normalize(identity);

        Assert.Equal("apple-stable-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@privaterelay.appleid.com", identity.FindFirst(ClaimTypes.Email)?.Value);
        var verified = Assert.Single(identity.FindAll("email_verified"));
        Assert.Equal(bool.TrueString, verified.Value);
        Assert.Equal(ClaimValueTypes.Boolean, verified.ValueType);
        Assert.Equal(AppleAuthenticationDefaults.Issuer, verified.Issuer);
    }

    [Theory]
    [InlineData(null, "true")]
    [InlineData("invalid-email", "true")]
    [InlineData("user@example.test", null)]
    [InlineData("user@example.test", "false")]
    [InlineData("user@example.test", "not-a-boolean")]
    public void ClaimNormalization_FailsClosedWithoutValidVerifiedEmail(string? email, string? emailVerified)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "apple-stable-subject") };
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));
        if (emailVerified is not null) claims.Add(new Claim("email_verified", emailVerified));
        var identity = new ClaimsIdentity(claims);

        AppleIdentityClaims.Normalize(identity);

        Assert.Equal("apple-stable-subject", identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Null(identity.FindFirst(ClaimTypes.Email));
        Assert.Null(identity.FindFirst("email_verified"));
    }

    private WebApplicationFactory<Program> ConfigureFactory(AppleTestConfiguration configuration)
        => factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:Apple:ClientId", configuration.ClientId ?? string.Empty);
            builder.UseSetting("Authentication:Apple:TeamId", configuration.TeamId ?? string.Empty);
            builder.UseSetting("Authentication:Apple:KeyId", configuration.KeyId ?? string.Empty);
            builder.UseSetting("Authentication:Apple:PrivateKeyPath", configuration.PrivateKeyPath ?? string.Empty);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IdentityDbContext>();
                services.RemoveAll<DbContextOptions<IdentityDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
                services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase($"{nameof(AppleAuthenticationTests)}-{Guid.NewGuid():N}"); options.UseOpenIddict(); });
            });
        });

    private static async Task<string> CreatePrivateKeyFileAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"apple-private-key-{Guid.NewGuid():N}.p8");
        await File.WriteAllTextAsync(path, PrivateKey);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        return path;
    }

    private sealed record AppleTestConfiguration(string? ClientId, string? TeamId, string? KeyId, string? PrivateKeyPath);
}
