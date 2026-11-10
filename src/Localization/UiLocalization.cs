using System.Globalization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;

namespace PocketLedger.Localization;

internal static class UiLocalization
{
    internal static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>
    {
        ["en"] = "English",
        ["hu"] = "Magyar",
        ["de"] = "Deutsch",
        ["fr"] = "Français"
    };

    internal static void AddUiLocalization(this WebApplicationBuilder builder)
    {
        builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
        builder.Services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = Languages.Keys.ToArray();
            options.SetDefaultCulture("en").AddSupportedUICultures(cultures);
            options.RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
        });
    }

    internal static void MapUiLanguageSelection(this WebApplication app)
    {
        app.MapPost("/ui-language", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var language = form["language"].ToString();
            if (!Languages.ContainsKey(language)) return Results.BadRequest();

            context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en", language)),
                new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps, Path = "/", MaxAge = TimeSpan.FromDays(365) });

            var returnUrl = form["returnUrl"].ToString();
            return Results.LocalRedirect(IsLocalPath(returnUrl) ? returnUrl : "/");
        }).AllowAnonymous();
    }

    private static bool IsLocalPath(string path) =>
        path.StartsWith('/') && (path.Length == 1 || path[1] is not ('/' or '\\')) && !path.Any(char.IsControl);

    internal static string LanguageLabel => CultureInfo.CurrentUICulture.Name switch
    {
        "hu" => "Nyelv",
        "de" => "Sprache",
        "fr" => "Langue",
        _ => "Language"
    };

    internal static string ApplyLabel => CultureInfo.CurrentUICulture.Name switch
    {
        "hu" => "Alkalmaz",
        "de" => "Übernehmen",
        "fr" => "Appliquer",
        _ => "Apply"
    };
}
