using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PocketLedger.Security;

namespace PocketLedger.Api.Tests;

public sealed class UserSessionKeyMiddlewareTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string SessionId = "8c1b8e4f0d2a4c6e9b7a5d3f1e0c2b4a";

    [Fact]
    public async Task RequestWithoutDataKeyHeaders_RunsWithoutAKey()
    {
        UserDataKey? seen = null;
        var context = CreateContext(authenticated: true, SessionId);

        await new UserSessionKeyMiddleware(_ => { seen = UserKeyContext.Current; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.Null(seen);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task ValidHeaders_OpenTheUsersKeyForThisRequestOnly()
    {
        using var dataKey = UserDataKey.Generate(UserId);
        var ciphertext = UserFieldCipher.EncryptString("note", dataKey, "Note");
        var (sessionKey, wrappedKey) = Wrap(dataKey, SessionId);
        var context = CreateContext(authenticated: true, SessionId, sessionKey, wrappedKey);
        string? decrypted = null;
        var headersVisibleDownstream = true;

        await new UserSessionKeyMiddleware(inner =>
        {
            decrypted = UserFieldCipher.DecryptString(ciphertext, UserKeyContext.Required, "Note");
            headersVisibleDownstream = inner.Request.Headers.ContainsKey(UserSessionKeyProtocol.SessionKeyHeader) || inner.Request.Headers.ContainsKey(UserSessionKeyProtocol.WrappedKeyHeader);
            return Task.CompletedTask;
        }).InvokeAsync(context);

        Assert.Equal("note", decrypted);
        Assert.False(headersVisibleDownstream);
        Assert.Null(UserKeyContext.Current);
    }

    [Theory]
    [InlineData(true, "another-session", false)]
    [InlineData(false, SessionId, false)]
    [InlineData(true, SessionId, true)]
    public async Task MismatchedOrDamagedKeys_AreRejectedBeforeTheRequestRuns(bool authenticated, string tokenSessionId, bool tamper)
    {
        using var dataKey = UserDataKey.Generate(UserId);
        var (sessionKey, wrappedKey) = Wrap(dataKey, SessionId);
        if (tamper) wrappedKey = wrappedKey[..^2] + (wrappedKey[^2] == 'A' ? "B" : "A") + wrappedKey[^1];
        var context = CreateContext(authenticated, tokenSessionId, sessionKey, wrappedKey);
        var ran = false;

        await new UserSessionKeyMiddleware(_ => { ran = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(ran);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AnotherUsersAccessToken_CannotOpenTheKey()
    {
        using var dataKey = UserDataKey.Generate(UserId);
        var (sessionKey, wrappedKey) = Wrap(dataKey, SessionId);
        var context = CreateContext(authenticated: true, SessionId, sessionKey, wrappedKey, userId: Guid.NewGuid());
        var ran = false;

        await new UserSessionKeyMiddleware(_ => { ran = true; return Task.CompletedTask; }).InvokeAsync(context);

        Assert.False(ran);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    private static (string SessionKey, string WrappedKey) Wrap(UserDataKey key, string sessionId)
    {
        var sessionKey = EphemeralKeyWrap.GenerateWrappingKey();
        return (UserSessionKeyProtocol.Encode(sessionKey), UserSessionKeyProtocol.Encode(EphemeralKeyWrap.Wrap(key, sessionKey, EphemeralKeyPurpose.Session, sessionId)));
    }

    private static DefaultHttpContext CreateContext(bool authenticated, string sessionId, string? sessionKey = null, string? wrappedKey = null, Guid? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, (userId ?? UserId).ToString()), new Claim(UserSessionKeyProtocol.SessionIdClaim, sessionId) };
        context.User = new ClaimsPrincipal(authenticated ? new ClaimsIdentity(claims, "Bearer") : new ClaimsIdentity());
        if (sessionKey is not null) context.Request.Headers[UserSessionKeyProtocol.SessionKeyHeader] = sessionKey;
        if (wrappedKey is not null) context.Request.Headers[UserSessionKeyProtocol.WrappedKeyHeader] = wrappedKey;
        return context;
    }
}

public sealed class ApiExceptionMiddlewareTests
{
    [Fact]
    public async Task MissingDataKey_EvenWhenWrappedByEf_AsksTheClientToSignInAgain()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ApiExceptionMiddleware(_ => throw new InvalidOperationException("An exception occurred while reading a database value.", new UserKeyUnavailableException()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Contains("data_key_required", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }
}
