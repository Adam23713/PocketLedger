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
            options.SetDefaultCulture("en").AddSupportedCultures("en").AddSupportedUICultures(cultures);
            options.RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()];
        });
    }

    internal static RouteHandlerBuilder MapUiLanguageSelection(this WebApplication app, Func<HttpContext, string, Task>? saveLanguage = null)
    {
        return app.MapPost("/ui-language", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await antiforgery.IsRequestValidAsync(context)) return Results.BadRequest();

            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var language = form["language"].ToString();
            if (!Languages.ContainsKey(language)) return Results.BadRequest();

            if (saveLanguage is not null) await saveLanguage(context, language);
            SetCookie(context, language);

            if (!form.TryGetValue("returnUrl", out var returnPath)) return Results.NoContent();
            var returnUrl = returnPath.ToString();
            return Results.LocalRedirect(IsLocalPath(returnUrl) ? returnUrl : "/");
        });
    }

    internal static void SetCookie(HttpContext context, string language)
    {
        var domain = context.RequestServices.GetRequiredService<IConfiguration>()["Localization:CookieDomain"];
        var secure = context.Request.IsHttps || context.RequestServices.GetRequiredService<IWebHostEnvironment>().IsProduction();
        context.Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture("en", language)),
            new CookieOptions { Domain = string.IsNullOrWhiteSpace(domain) ? null : domain, HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = secure, Path = "/", MaxAge = TimeSpan.FromDays(365) });
    }

    internal static void UseLanguage(string? language)
    {
        if (language is not null && Languages.ContainsKey(language))
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
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
}
