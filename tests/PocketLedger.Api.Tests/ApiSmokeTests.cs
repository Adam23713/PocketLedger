using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Api.Tests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<PocketLedgerDbContext>();
            services.RemoveAll<DbContextOptions<PocketLedgerDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PocketLedgerDbContext>>();
            services.AddDbContext<PocketLedgerDbContext>(options => options.UseInMemoryDatabase(nameof(ApiSmokeTests)));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    });

    [Fact]
    public async Task Health_IsPublic()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task VersionedAccountsEndpoint_UsesAuthenticatedOwner()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/v1/accounts");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MainBalanceEndpoint_ReturnsSeparateCurrencyTotalsAndExcludesDisabledAccounts()
    {
        var ownerId = Guid.Parse("a3b4bceb-f37e-49f4-b726-b8e40d7f34d3");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PocketLedgerDbContext>();
            await db.Database.EnsureDeletedAsync();
            db.Accounts.AddRange(
                new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Forint", Type = AccountType.BankAccount, Currency = "HUF", InitialBalance = 100m, IncludeInMainBalance = true },
                new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Euro", Type = AccountType.BankAccount, Currency = "EUR", InitialBalance = 100m, IncludeInMainBalance = true },
                new Account { Id = Guid.NewGuid(), OwnerId = ownerId, Name = "Excluded", Type = AccountType.Cash, Currency = "USD", InitialBalance = 500m, IncludeInMainBalance = false });
            await db.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var balances = await client.GetFromJsonAsync<IReadOnlyList<CurrencyBalanceDto>>("/api/v1/transactions/main-balance");

        Assert.Equal([new CurrencyBalanceDto("EUR", 100m), new CurrencyBalanceDto("HUF", 100m)], balances);

        using var cleanupScope = factory.Services.CreateScope();
        await cleanupScope.ServiceProvider.GetRequiredService<PocketLedgerDbContext>().Database.EnsureDeletedAsync();
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.Parse("a3b4bceb-f37e-49f4-b726-b8e40d7f34d3").ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
