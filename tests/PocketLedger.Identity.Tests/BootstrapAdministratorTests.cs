using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Identity.Tests;

public sealed class BootstrapAdministratorTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public BootstrapAdministratorTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(BootstrapAdministratorTests)); options.UseOpenIddict(); });
        });
    });

    [Fact]
    public async Task BootstrapFactoryCreatesOnlyAdministratorUser()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var administrator = ApplicationUser.CreateBootstrapAdministrator("bootstrap-admin");
        var laterUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "later-user" };

        Assert.True((await users.CreateAsync(administrator)).Succeeded);
        Assert.True((await users.CreateAsync(laterUser)).Succeeded);

        Assert.True((await users.FindByNameAsync("bootstrap-admin"))!.IsBootstrapAdministrator);
        Assert.False((await users.FindByNameAsync("later-user"))!.IsBootstrapAdministrator);
    }

    [Fact]
    public async Task AdministratorClaimControlsNamedPolicy()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principalFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var administrator = ApplicationUser.CreateBootstrapAdministrator("policy-admin");
        var regularUser = new ApplicationUser { Id = Guid.NewGuid(), UserName = "policy-user" };
        Assert.True((await users.CreateAsync(administrator)).Succeeded);
        Assert.True((await users.CreateAsync(regularUser)).Succeeded);

        var administratorPrincipal = await principalFactory.CreateAsync(administrator);
        var regularPrincipal = await principalFactory.CreateAsync(regularUser);

        Assert.Equal(bool.TrueString, administratorPrincipal.FindFirst(BootstrapAdministratorAuthorization.ClaimType)?.Value);
        Assert.Null(regularPrincipal.FindFirst(BootstrapAdministratorAuthorization.ClaimType));
        Assert.True((await authorization.AuthorizeAsync(administratorPrincipal, null, BootstrapAdministratorAuthorization.PolicyName)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(regularPrincipal, null, BootstrapAdministratorAuthorization.PolicyName)).Succeeded);
    }

    [Fact]
    public void MigrationSelectsAdministratorDeterministicallyAndCreatesUniqueConstraint()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql("Host=localhost;Database=not-used;Username=not-used;Password=not-used").Options;
        using var db = new IdentityDbContext(options);

        var sql = db.Database.GetService<IMigrator>().GenerateScript("20260914172003_EncryptIdentitySecrets", "20261006105243_AddBootstrapAdministrator");

        Assert.Contains("ORDER BY \"CreatedAtUtc\", \"Id\"", sql);
        Assert.Contains("SET \"IsBootstrapAdministrator\" = TRUE", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_AspNetUsers_IsBootstrapAdministrator\"", sql);
        Assert.Contains("WHERE \"IsBootstrapAdministrator\" = TRUE", sql);
    }
}
