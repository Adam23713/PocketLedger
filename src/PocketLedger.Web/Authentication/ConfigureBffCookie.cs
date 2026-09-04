using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace PocketLedger.Web.Authentication;

public sealed class ConfigureBffCookie(DatabaseTicketStore store) : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == "BffCookie") options.SessionStore = store;
    }
}
