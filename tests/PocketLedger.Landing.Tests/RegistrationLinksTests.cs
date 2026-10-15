using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PocketLedger.Landing.Tests;

public sealed class RegistrationLinksTests
{
    [Fact]
    public async Task LandingPage_ShowsRegistrationLinksWhenRegistrationIsEnabled()
    {
        await using var factory = CreateFactory(true);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var privacyHtml = await client.GetStringAsync("/privacy");

        Assert.Contains("Public registration is open", html);
        Assert.Contains("href=\"https://identity.example.test/Account/Register\"", html);
        Assert.Contains("Create account", html);
        Assert.Contains("href=\"https://identity.example.test/Account/Register\"", privacyHtml);
    }

    [Fact]
    public async Task LandingPage_HidesRegistrationLinksWhenRegistrationIsDisabled()
    {
        await using var factory = CreateFactory(false);
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("Public registration is currently closed", html);
        Assert.DoesNotContain("/Account/Register", html);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool registrationEnabled) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Landing:AppBaseUrl", "https://app.example.test");
        builder.UseSetting("Landing:IdentityBaseUrl", "https://identity.example.test");
        builder.UseSetting("Registration:Enabled", registrationEnabled.ToString());
    });
}
