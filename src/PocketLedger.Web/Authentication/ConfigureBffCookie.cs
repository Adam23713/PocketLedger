using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace PocketLedger.Web.Authentication;

public sealed class ConfigureBffCookie(DatabaseTicketStore store, UserSessionKeys sessionKeys) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name != "BffCookie") return;
        options.SessionStore = store;
        // Every way out of the BFF session (logout, revocation, expiry) also drops the browser's half of the data key.
        var signingOut = options.Events.OnSigningOut;
        options.Events.OnSigningOut = async context =>
        {
            sessionKeys.Clear(context.HttpContext);
            await signingOut(context);
        };
    }
}
