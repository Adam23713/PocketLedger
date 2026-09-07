var builder = WebApplication.CreateBuilder(args);
var appBaseUrl = builder.Configuration["Landing:AppBaseUrl"] ?? "https://app.pocketledger.dev";
if (!Uri.TryCreate(appBaseUrl, UriKind.Absolute, out var appUri) || (appUri.Scheme != "https" && !(builder.Environment.IsDevelopment() && appUri.Scheme == "http")) || appUri.AbsolutePath != "/" || !string.IsNullOrEmpty(appUri.Query) || !string.IsNullOrEmpty(appUri.Fragment) || !string.IsNullOrEmpty(appUri.UserInfo))
    throw new InvalidOperationException("Landing:AppBaseUrl must be an HTTPS origin (HTTP is allowed in Development).");
builder.Configuration["Landing:AppBaseUrl"] = appUri.GetLeftPart(UriPartial.Authority);
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
