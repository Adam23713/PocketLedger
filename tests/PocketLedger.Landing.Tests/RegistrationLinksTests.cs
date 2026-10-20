using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Landing;

namespace PocketLedger.Landing.Tests;

public sealed class RegistrationLinksTests
{
    [Fact]
    public async Task LandingPage_ShowsRegistrationLinksWhenRegistrationIsEnabled()
    {
        await using var factory = CreateFactory(new RegistrationStatus(true, null, null));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var privacyHtml = await client.GetStringAsync("/privacy");

        Assert.Contains("Public registration is open — <a", html);
        Assert.Contains("href=\"https://app.example.test/Session/Register\"", html);
        Assert.Contains("Create account", html);
        Assert.Contains("href=\"https://app.example.test/Session/Register\"", privacyHtml);
    }

    [Fact]
    public async Task LandingPage_ShowsRemainingAccountsWhenRegistrationIsLimited()
    {
        await using var factory = CreateFactory(new RegistrationStatus(true, 20, 7));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var privacyHtml = await client.GetStringAsync("/privacy");

        Assert.Contains("Public registration is open — 7 of 20 spots left — <a", html);
        Assert.Contains("href=\"https://app.example.test/Session/Register\"", html);
        Assert.Contains("7 of 20 spots left", privacyHtml);
    }

    [Fact]
    public async Task LandingPage_HidesRegistrationLinksWhenAllAccountsAreTaken()
    {
        await using var factory = CreateFactory(new RegistrationStatus(false, 20, 0));
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Public registration is open, but all 20 spots are currently taken", html);
        Assert.DoesNotContain("/Session/Register", html);
    }

    [Fact]
    public async Task LandingPage_HidesRegistrationLinksWhenRegistrationIsDisabled()
    {
        await using var factory = CreateFactory(RegistrationStatus.Closed);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var privacyHtml = await client.GetStringAsync("/privacy");

        Assert.Contains("PocketLedger is still under active development. Public registration isn't open yet — it's coming soon.", html);
        Assert.DoesNotContain("/Session/Register", html);
        Assert.Contains("PocketLedger is still under active development. Public registration isn't open yet — it's coming soon.", privacyHtml);
        Assert.DoesNotContain("/Session/Register", privacyHtml);
    }

    private static WebApplicationFactory<Program> CreateFactory(RegistrationStatus status) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Landing:AppBaseUrl", "https://app.example.test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRegistrationStatusProvider>();
            services.AddSingleton<IRegistrationStatusProvider>(new FixedRegistrationStatusProvider(status));
        });
    });

    private sealed class FixedRegistrationStatusProvider(RegistrationStatus status) : IRegistrationStatusProvider
    {
        public Task<RegistrationStatus> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
    }
}
