using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PocketLedger.Landing;
using PocketLedger.Landing.Contact;

namespace PocketLedger.Landing.Tests;

public sealed partial class ContactFormTests
{
    private const string ValidMessage = "Hello! I would love to see budgets per category.";

    [Fact]
    public async Task ContactPage_RendersProtectedFormAndAllowsTurnstileOnlyThere()
    {
        await using var factory = CreateFactory(new RecordingMailSender(), new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient();

        var contact = await client.GetAsync("/contact");
        var html = await contact.Content.ReadAsStringAsync();
        var home = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, contact.StatusCode);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("name=\"Website\"", html);
        Assert.Contains("class=\"cf-turnstile\" data-sitekey=\"test-site-key\" data-action=\"contact\"", html);
        Assert.Contains("https://challenges.cloudflare.com/turnstile/v0/api.js", html);
        var contactPolicy = contact.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self' https://challenges.cloudflare.com", contactPolicy);
        Assert.Contains("form-action 'self'", contactPolicy);
        var homePolicy = home.Headers.GetValues("Content-Security-Policy").Single();
        Assert.DoesNotContain("challenges.cloudflare.com", homePolicy);
        Assert.Contains("form-action 'none'", homePolicy);
    }

    [Fact]
    public async Task ValidSubmission_SendsMailAndRedirectsToConfirmation()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostAsync(client, "visitor@example.com", ValidMessage);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/contact?sent=True", response.Headers.Location?.OriginalString);
        var submission = Assert.Single(sender.Submissions);
        Assert.Equal("visitor@example.com", submission.Email);
        Assert.Equal(ValidMessage, submission.Message);
    }

    [Fact]
    public async Task FilledHoneypot_PretendsSuccessWithoutSending()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await PostAsync(client, "visitor@example.com", ValidMessage, website: "https://spam.example");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task FailedTurnstile_IsRejected()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(false));
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "visitor@example.com", ValidMessage);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("anti-spam check", await response.Content.ReadAsStringAsync());
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task MarkupInMessage_IsRejectedAndNotEchoedUnencoded()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "visitor@example.com", "Hi there <script>alert(1)</script>");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("plain text only", html);
        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task RepeatedAttemptsFromOneClient_AreRateLimited()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true), perClientPerHour: 2);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await PostAsync(client, "first@example.com", ValidMessage);
        await PostAsync(client, "not-an-email", ValidMessage);
        var third = await PostAsync(client, "third@example.com", ValidMessage);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Single(sender.Submissions);
    }

    [Fact]
    public async Task SubmissionWithoutValidFormStamp_IsRejected()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient();

        var response = await PostAsync(client, "visitor@example.com", ValidMessage, formStamp: "forged");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task SubmissionWithoutAntiforgeryToken_IsRejected()
    {
        var sender = new RecordingMailSender();
        await using var factory = CreateFactory(sender, new FixedTurnstileVerifier(true));
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "visitor@example.com", ["Message"] = ValidMessage
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(sender.Submissions);
    }

    [Fact]
    public async Task ContactPage_IsDisabledOutsideDevelopmentWithoutSmtp()
    {
        await using var factory = CreateFactory(new RecordingMailSender(), new FixedTurnstileVerifier(true), environment: "Production");
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/contact");

        Assert.Contains("The contact form is resting for a moment.", html);
        Assert.DoesNotContain("cf-turnstile", html);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string email, string message, string website = "", string? formStamp = null)
    {
        var form = await client.GetStringAsync("/contact");
        return await client.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = HiddenValue(form, "__RequestVerificationToken"),
            ["FormStamp"] = formStamp ?? HiddenValue(form, "FormStamp"),
            ["Email"] = email,
            ["Message"] = message,
            ["Website"] = website,
            [Pages.ContactModel.TurnstileResponseField] = "token"
        }));
    }

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, $"Hidden field {name} was not rendered.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static WebApplicationFactory<Program> CreateFactory(IContactMailSender sender, ITurnstileVerifier turnstile, int perClientPerHour = 10, string environment = "Development") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Landing:AppBaseUrl", "https://app.example.test");
            builder.UseSetting("Contact:Turnstile:SiteKey", "test-site-key");
            builder.UseSetting("Contact:Turnstile:SecretKey", "test-secret-key");
            builder.UseSetting("Contact:Limits:MinimumFillSeconds", "0");
            builder.UseSetting("Contact:Limits:PerClientPerHour", perClientPerHour.ToString());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRegistrationStatusProvider>();
                services.AddSingleton<IRegistrationStatusProvider>(new FixedRegistrationStatusProvider(RegistrationStatus.Closed));
                services.RemoveAll<IContactMailSender>();
                services.AddSingleton(sender);
                services.RemoveAll<ITurnstileVerifier>();
                services.AddSingleton(turnstile);
            });
        });

    private sealed class RecordingMailSender : IContactMailSender
    {
        public List<ContactSubmission> Submissions { get; } = [];

        public Task SendAsync(ContactSubmission submission, CancellationToken cancellationToken = default)
        {
            lock (Submissions) Submissions.Add(submission);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTurnstileVerifier(bool result) : ITurnstileVerifier
    {
        public Task<bool> VerifyAsync(string? token, string? clientIp, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class FixedRegistrationStatusProvider(RegistrationStatus status) : IRegistrationStatusProvider
    {
        public Task<RegistrationStatus> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(status);
    }
}
