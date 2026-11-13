using PocketLedger.Localization;
var builder = WebApplication.CreateBuilder(args);
builder.AddUiLocalization();
builder.Services.AddRazorPages().AddViewLocalization();

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; connect-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    await next();
});
app.UseStaticFiles();
app.UseRequestLocalization();
app.MapRazorPages();
app.MapUiLanguageSelection().AllowAnonymous();
app.Run();

public partial class Program;
