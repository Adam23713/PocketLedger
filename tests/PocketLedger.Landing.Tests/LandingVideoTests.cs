using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Landing;

namespace PocketLedger.Landing.Tests;

public sealed class LandingVideoTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=2PuFyjAs7JA")]
    [InlineData("https://youtu.be/2PuFyjAs7JA?si=abc")]
    [InlineData("https://www.youtube.com/embed/2PuFyjAs7JA")]
    [InlineData("https://youtube.com/shorts/2PuFyjAs7JA")]
    public void GetEmbedUrl_AcceptsYouTubeLinks(string url)
        => Assert.Equal("https://www.youtube-nocookie.com/embed/2PuFyjAs7JA", YouTubeVideo.GetEmbedUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://www.youtube.com/watch?v=2PuFyjAs7JA")]
    [InlineData("https://example.com/watch?v=2PuFyjAs7JA")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/watch?v=2PuFyjAs7JA\"><script>")]
    public void GetEmbedUrl_RejectsOtherValues(string? url) => Assert.Null(YouTubeVideo.GetEmbedUrl(url));

    [Fact]
    public async Task LandingPage_EmbedsConfiguredVideoAndAllowsOnlyItsFrameOrigin()
    {
        await using var factory = CreateFactory("https://www.youtube.com/watch?v=2PuFyjAs7JA");
        using var client = factory.CreateClient();

        var home = await client.GetAsync("/");
        var privacy = await client.GetAsync("/privacy");

        var html = await home.Content.ReadAsStringAsync();
        // The video is only a placeholder until the visitor clicks play, so the page itself makes no request to YouTube.
        Assert.Contains("data-video-src=\"https://www.youtube-nocookie.com/embed/2PuFyjAs7JA?autoplay=1\"", html);
        Assert.DoesNotContain("<iframe", html);
        Assert.Contains("(YouTube)", await privacy.Content.ReadAsStringAsync());
        Assert.Contains("frame-src https://www.youtube-nocookie.com;", home.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("frame-src 'none'", privacy.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task LandingPage_HidesVideoWhenNotConfigured()
    {
        await using var factory = CreateFactory("");
        using var client = factory.CreateClient();

        var home = await client.GetAsync("/");

        Assert.DoesNotContain("video-section", await home.Content.ReadAsStringAsync());
        Assert.Contains("frame-src 'none'", home.Headers.GetValues("Content-Security-Policy").Single());
    }

    private static WebApplicationFactory<Program> CreateFactory(string videoUrl) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Landing:AppBaseUrl", "https://app.example.test");
        builder.UseSetting("Landing:YouTubeVideoUrl", videoUrl);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRegistrationStatusProvider>();
            services.AddSingleton<IRegistrationStatusProvider>(new FixedRegistrationStatusProvider());
        });
    });

    private sealed class FixedRegistrationStatusProvider : IRegistrationStatusProvider
    {
        public Task<RegistrationStatus> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(new RegistrationStatus(true, null, null));
    }
}
