var builder = WebApplication.CreateBuilder(args);
var appUri = GetOrigin(builder.Configuration["Landing:AppBaseUrl"] ?? "https://app.pocketledger.dev", "Landing:AppBaseUrl", builder.Environment);
var identityUri = GetOrigin(builder.Configuration["Landing:IdentityBaseUrl"] ?? "https://identity.pocketledger.dev", "Landing:IdentityBaseUrl", builder.Environment);
builder.Configuration["Landing:AppBaseUrl"] = appUri.GetLeftPart(UriPartial.Authority);
builder.Configuration["Landing:IdentityBaseUrl"] = identityUri.GetLeftPart(UriPartial.Authority);
builder.Services.AddRazorPages();
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'self' {appUri.GetLeftPart(UriPartial.Authority)}; frame-ancestors 'none'; base-uri 'self'; form-action 'none'";
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
