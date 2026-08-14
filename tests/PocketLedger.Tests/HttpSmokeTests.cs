using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Data;

namespace PocketLedger.Tests;

public class HttpSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public HttpSmokeTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PocketLedgerDbContext>();
            services.RemoveAll<DbContextOptions<PocketLedgerDbContext>>();
            services.AddDbContext<PocketLedgerDbContext>(options => options.UseInMemoryDatabase("HttpSmokeTests"));
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

        Assert.True(response.IsSuccessStatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Transactions")]
    [InlineData("/ImportExport")]
    public async Task ProtectedPages_RedirectAnonymousUsersToLogin(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Account/Login", response.Headers.Location?.AbsolutePath);
    }
}
