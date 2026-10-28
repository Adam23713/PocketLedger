using PocketLedger.Landing;
using PocketLedger.Landing.Contact;

var builder = WebApplication.CreateBuilder(args);
var appUri = GetOrigin(builder.Configuration["Landing:AppBaseUrl"] ?? "https://app.pocketledger.dev", "Landing:AppBaseUrl", builder.Environment);
builder.Configuration["Landing:AppBaseUrl"] = appUri.GetLeftPart(UriPartial.Authority);
var youTubeEmbedUrl = YouTubeVideo.GetEmbedUrl(builder.Configuration["Landing:YouTubeVideoUrl"]);
builder.Configuration["Landing:YouTubeEmbedUrl"] = youTubeEmbedUrl;
builder.Services.AddRazorPages();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IRegistrationStatusProvider, RegistrationStatusProvider>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Identity:InternalBaseUrl"] ?? "http://localhost:5052/");
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.Configure<ContactOptions>(builder.Configuration.GetSection(ContactOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IContactRateLimiter, ContactRateLimiter>();
builder.Services.AddSingleton<ContactFormTimestamp>();
builder.Services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(client =>
{
    client.BaseAddress = new Uri("https://challenges.cloudflare.com/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
var smtpConfigured = builder.Configuration.GetSection(ContactOptions.SectionName).Get<ContactOptions>()?.Smtp.IsConfigured == true;
if (!smtpConfigured && builder.Environment.IsDevelopment()) builder.Services.AddSingleton<IContactMailSender, LoggingContactMailSender>();
else builder.Services.AddSingleton<IContactMailSender, SmtpContactMailSender>();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    // Only the contact page loads Cloudflare Turnstile and submits a form; every other page keeps the tighter policy.
    var contactPage = context.Request.Path.Equals("/contact", StringComparison.OrdinalIgnoreCase);
    var turnstile = contactPage ? " https://challenges.cloudflare.com" : string.Empty;
    var formAction = contactPage ? "'self'" : "'none'";
    // The home page may embed the configured YouTube video; no other page loads frames from YouTube.
    var homePage = context.Request.Path == "/" || context.Request.Path.Equals("/index", StringComparison.OrdinalIgnoreCase);
    var frameSources = contactPage ? turnstile : homePage && youTubeEmbedUrl is not null ? " " + YouTubeVideo.EmbedOrigin : " 'none'";
    context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self'{turnstile}; frame-src{frameSources}; style-src 'self'; img-src 'self'; connect-src 'self' {appUri.GetLeftPart(UriPartial.Authority)}; frame-ancestors 'none'; base-uri 'self'; form-action {formAction}";
    await next();
});
app.UseStaticFiles();
app.MapRazorPages();
app.Run();

static Uri GetOrigin(string value, string configurationKey, IWebHostEnvironment environment)
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(environment.IsDevelopment() && uri.Scheme == "http")) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        throw new InvalidOperationException($"{configurationKey} must be an HTTPS origin (HTTP is allowed in Development).");
    return uri;
}

public partial class Program;
