using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using PocketLedger.Security;

namespace PocketLedger.Web.Authentication;

public sealed record UserSessionKeyHeaders(string SessionKey, string WrappedKey);

// Splits the session's data key between browser and server: W = AES-GCM(SK, DEK) lives in the server-side BFF ticket,
// SK only in an HttpOnly cookie. Neither the Web database nor a stolen cookie alone can open the user's data.
public sealed class UserSessionKeys(IDataProtectionProvider protectionProvider, IHostEnvironment environment)
{
    public const string CookieName = "PocketLedger.Web.DataKey";
    public const string WrappedKeyItem = ".pl-wrapped-data-key";
    public const string ProtectorPurpose = "PocketLedger.Web.UserSessionKey.v1";
    private readonly IDataProtector protector = protectionProvider.CreateProtector(ProtectorPurpose);

    // Runs on the code exchange: keeps W in the ticket being created and SK in the browser, and strips both from the response.
    public void Accept(TokenResponseReceivedContext context)
    {
        var response = context.TokenEndpointResponse;
        var sessionKey = response.GetParameter(UserSessionKeyProtocol.SessionKeyParameter);
        var wrappedKey = response.GetParameter(UserSessionKeyProtocol.WrappedKeyParameter);
        response.Parameters.Remove(UserSessionKeyProtocol.SessionKeyParameter);
        response.Parameters.Remove(UserSessionKeyProtocol.WrappedKeyParameter);
        if (UserSessionKeyProtocol.TryDecode(sessionKey)?.Length != EphemeralKeyWrap.WrappingKeySize || UserSessionKeyProtocol.TryDecode(wrappedKey) is null)
        {
            context.Fail("The token response did not include the session's data key.");
            return;
        }
        context.Properties!.Items[WrappedKeyItem] = wrappedKey;
        context.HttpContext.Response.Cookies.Append(CookieName, protector.Protect(sessionKey!), CreateCookieOptions(context.HttpContext));
    }

    public UserSessionKeyHeaders? Read(HttpContext context, AuthenticationProperties properties)
    {
        if (!properties.Items.TryGetValue(WrappedKeyItem, out var wrappedKey) || string.IsNullOrEmpty(wrappedKey)) return null;
        if (!context.Request.Cookies.TryGetValue(CookieName, out var protectedKey) || string.IsNullOrEmpty(protectedKey)) return null;
        try
        {
            return new UserSessionKeyHeaders(protector.Unprotect(protectedKey), wrappedKey);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Clear(HttpContext context)
    {
        if (context.Request.Cookies.ContainsKey(CookieName)) context.Response.Cookies.Delete(CookieName, CreateCookieOptions(context));
    }

    // A browser-session cookie, like the BFF cookie it accompanies.
    private CookieOptions CreateCookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment() || context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true
    };
}
