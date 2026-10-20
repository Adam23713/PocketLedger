using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using PocketLedger.Security;
using PocketLedger.Web.Authentication;

namespace PocketLedger.Tests;

public sealed class UserSessionKeysTests
{
    [Fact]
    public void Accept_KeepsTheWrappedKeyInTheTicketAndTheSessionKeyOnlyInTheBrowser()
    {
        var keys = new UserSessionKeys(new EphemeralDataProtectionProvider(), new TestHostEnvironment());
        var sessionKey = UserSessionKeyProtocol.Encode(EphemeralKeyWrap.GenerateWrappingKey());
        var context = CreateTokenResponseContext(sessionKey, "d3JhcHBlZC1rZXk");

        keys.Accept(context);

        Assert.Null(context.Result);
        Assert.Equal("d3JhcHBlZC1rZXk", context.Properties!.Items[UserSessionKeys.WrappedKeyItem]);
        Assert.DoesNotContain(context.Properties.Items.Values, value => value == sessionKey);
        Assert.Null(context.TokenEndpointResponse.GetParameter(UserSessionKeyProtocol.SessionKeyParameter));
        Assert.Null(context.TokenEndpointResponse.GetParameter(UserSessionKeyProtocol.WrappedKeyParameter));
        var setCookie = context.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.StartsWith(UserSessionKeys.CookieName + "=", setCookie);
        Assert.DoesNotContain(sessionKey, setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

        var nextRequest = new DefaultHttpContext();
        nextRequest.Request.Headers.Cookie = setCookie.Split(';')[0];
        Assert.Equal(new UserSessionKeyHeaders(sessionKey, "d3JhcHBlZC1rZXk"), keys.Read(nextRequest, context.Properties));
    }

    [Fact]
    public void Accept_WithoutTheSessionDataKey_FailsTheSignIn()
    {
        var keys = new UserSessionKeys(new EphemeralDataProtectionProvider(), new TestHostEnvironment());
        var context = CreateTokenResponseContext(null, null);

        keys.Accept(context);

        Assert.NotNull(context.Result?.Failure);
        Assert.False(context.Properties!.Items.ContainsKey(UserSessionKeys.WrappedKeyItem));
        Assert.Equal(0, context.HttpContext.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public void Read_RejectsAForgedCookie()
    {
        var keys = new UserSessionKeys(new EphemeralDataProtectionProvider(), new TestHostEnvironment());
        var properties = new AuthenticationProperties();
        properties.Items[UserSessionKeys.WrappedKeyItem] = "d3JhcHBlZC1rZXk";
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{UserSessionKeys.CookieName}=forged";

        Assert.Null(keys.Read(context, properties));
    }

    [Fact]
    public async Task SigningOutOfTheBffSession_DeletesTheSessionKeyCookie()
    {
        var keys = new UserSessionKeys(new EphemeralDataProtectionProvider(), new TestHostEnvironment());
        var options = new CookieAuthenticationOptions();
        new ConfigureBffCookie(null!, keys).PostConfigure("BffCookie", options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = $"{UserSessionKeys.CookieName}=value";

        await options.Events.OnSigningOut(new CookieSigningOutContext(httpContext, new AuthenticationScheme("BffCookie", null, typeof(CookieAuthenticationHandler)), options, new AuthenticationProperties(), new CookieOptions()));

        Assert.Contains(UserSessionKeys.CookieName + "=;", httpContext.Response.Headers.SetCookie.ToString());
    }

    private static TokenResponseReceivedContext CreateTokenResponseContext(string? sessionKey, string? wrappedKey)
    {
        var response = new OpenIdConnectMessage { AccessToken = "access-token" };
        if (sessionKey is not null) response.SetParameter(UserSessionKeyProtocol.SessionKeyParameter, sessionKey);
        if (wrappedKey is not null) response.SetParameter(UserSessionKeyProtocol.WrappedKeyParameter, wrappedKey);
        return new TokenResponseReceivedContext(new DefaultHttpContext(), new AuthenticationScheme("oidc", null, typeof(OpenIdConnectHandler)), new OpenIdConnectOptions(),
            new ClaimsPrincipal(new ClaimsIdentity()), new AuthenticationProperties())
        {
            TokenEndpointResponse = response
        };
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PocketLedger.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
