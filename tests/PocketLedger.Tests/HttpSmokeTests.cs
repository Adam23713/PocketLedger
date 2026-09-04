using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PocketLedger.Web.Data;

namespace PocketLedger.Tests;

public class HttpSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HttpSmokeTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<WebDbContext>();
            services.RemoveAll<DbContextOptions<WebDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<WebDbContext>>();
            services.AddDbContext<WebDbContext>(options => options.UseInMemoryDatabase("HttpSmokeTests"));
            services.Configure<OpenIdConnectOptions>("oidc", options => options.Configuration = new OpenIdConnectConfiguration
            {
                AuthorizationEndpoint = "https://identity.test/connect/authorize",
                TokenEndpoint = "https://identity.test/connect/token",
                EndSessionEndpoint = "https://identity.test/connect/logout",
                Issuer = "https://identity.test/"
            });
        });
    });

    [Theory]
    [InlineData("/")]
    [InlineData("/Transactions")]
    [InlineData("/ImportExport")]
    public async Task ProtectedPages_RedirectAnonymousUsersToLogin(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.True(response.StatusCode == System.Net.HttpStatusCode.Redirect, await response.Content.ReadAsStringAsync());
        Assert.Equal("identity.test", response.Headers.Location?.Host);
        Assert.Equal("/connect/authorize", response.Headers.Location?.AbsolutePath);
    }
}
