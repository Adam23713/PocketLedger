using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Data;

namespace PocketLedger.Identity.Tests;

public sealed class IdentitySmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public IdentitySmokeTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(IdentitySmokeTests)); options.UseOpenIddict(); });
        });
    });

    [Theory]
    [InlineData("/Account/Login")]
    [InlineData("/Account/TwoFactor")]
    [InlineData("/Account/RecoveryCode")]
    public async Task AnonymousAccountPages_AreReachable(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuthorizationEndpoint_RedirectsAnonymousUserToIdentityLogin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/connect/authorize?client_id=pocketledger-web&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A5050%2Fsignin-oidc&scope=openid&code_challenge=abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG&code_challenge_method=S256");
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        Assert.StartsWith("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task IdentityRoot_RedirectsAnonymousUserToLogin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task IdentityRoot_RedirectsAuthenticatedUserToWebDashboard()
    {
        var controller = new PocketLedger.Controllers.HomeController(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenIddict:WebBaseUrl"] = "https://app.example.test"
        }).Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([], "test"))
                }
            }
        };

        var result = Assert.IsType<RedirectResult>(controller.Index());

        Assert.Equal("https://app.example.test", result.Url);
    }
}
