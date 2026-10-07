using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Controllers;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Identity.Tests;

public sealed class AdminDashboardTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid CurrentUserId = Guid.Parse("95a8be08-bb94-4515-80b2-b1e628f3145f");
    private readonly WebApplicationFactory<Program> factory;

    public AdminDashboardTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("RequestTelemetry:ConnectionString", "");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => { options.UseInMemoryDatabase(nameof(AdminDashboardTests)); options.UseOpenIddict(); });
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, _ => { });
        });
    });

    [Fact]
    public void AdminControllerAndIndexAction_ExplicitlyRequireBootstrapAdministratorPolicy()
    {
        var controllerPolicy = typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>().Single().Policy;
        var actionPolicy = typeof(AdminController).GetMethod(nameof(AdminController.Index), BindingFlags.Instance | BindingFlags.Public)!
            .GetCustomAttributes<AuthorizeAttribute>().Single().Policy;

        Assert.Equal(BootstrapAdministratorAuthorization.PolicyName, controllerPolicy);
        Assert.Equal(BootstrapAdministratorAuthorization.PolicyName, actionPolicy);
    }

    [Fact]
    public async Task Index_RejectsAuthenticatedUserWithoutBootstrapAdministratorClaim()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Index_ReturnsTenDeterministicallyOrderedUsersWithoutSearch()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await SendAsAdministratorAsync(client, "/Admin?page=1");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        for (var index = 11; index >= 2; index--) Assert.Contains($"<tr><td>user-{index:00}</td>", content);
        Assert.DoesNotContain("<tr><td>user-01</td>", content);
        Assert.DoesNotContain("never-signed-in", content);
        Assert.True(content.IndexOf("user-11", StringComparison.Ordinal) < content.IndexOf("user-10", StringComparison.Ordinal));
        Assert.DoesNotContain("type=\"search\"", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page 1 of 2", content);
    }

    [Fact]
    public async Task Index_ClampsInvalidPageAndShowsNeverSignedInStateOnLastPage()
    {
        await SeedUsersAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var firstPage = await (await SendAsAdministratorAsync(client, "/Admin?page=0")).Content.ReadAsStringAsync();
        var lastPage = await (await SendAsAdministratorAsync(client, "/Admin?page=999")).Content.ReadAsStringAsync();

        Assert.Contains("Page 1 of 2", firstPage);
        Assert.Contains("Page 2 of 2", lastPage);
        Assert.Contains("<tr><td>user-01</td>", lastPage);
        Assert.Contains("never-signed-in", lastPage);
        Assert.Contains("Never signed in", lastPage);
        Assert.DoesNotContain("<tr><td>user-02</td>", lastPage);
    }

    private async Task SeedUsersAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        var start = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        dbContext.Users.AddRange(Enumerable.Range(1, 11).Select(index => new ApplicationUser
        {
            Id = index == 1 ? CurrentUserId : Guid.NewGuid(), UserName = $"user-{index:00}", NormalizedUserName = $"USER-{index:00}", LastSuccessfulLoginAtUtc = start.AddHours(index),
            TwoFactorEnabled = index == 1, AuthenticatorSetupComplete = index == 1
        }));
        dbContext.Users.Add(new ApplicationUser { Id = Guid.NewGuid(), UserName = "never-signed-in", NormalizedUserName = "NEVER-SIGNED-IN" });
        await dbContext.SaveChangesAsync();
    }

    private static Task<HttpResponseMessage> SendAsAdministratorAsync(HttpClient client, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthenticationHandler.AdministratorHeader, "true");
        return client.SendAsync(request);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "AdminDashboardTest";
        public const string AdministratorHeader = "X-Test-Bootstrap-Administrator";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, CurrentUserId.ToString()), new(ClaimTypes.Name, "user-01") };
            if (Request.Headers[AdministratorHeader] == "true") claims.Add(new Claim(BootstrapAdministratorAuthorization.ClaimType, bool.TrueString));
            var identity = new ClaimsIdentity(claims, IdentityConstants.ApplicationScheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
