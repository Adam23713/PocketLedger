using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using PocketLedger.Security;

namespace PocketLedger.Services;

public interface IPendingSignInKeys
{
    void Issue(HttpContext context, UserDataKey key);
    // Returns a copy of the pending DEK that the caller must dispose, or null when none is pending for this user.
    UserDataKey? TryOpen(HttpContext context, Guid userId);
    bool Has(HttpContext context, Guid userId);
    void Clear(HttpContext context);
}

// Holds the unlocked DEK between the password step and the end of sign-in. Process memory keeps only AES-GCM(T, DEK);
// T lives only in the browser's pending sign-in cookie. Identity runs as a single instance, so memory is the right store.
public sealed class PendingSignInKeys(IMemoryCache cache, IDataProtectionProvider protectionProvider, IHostEnvironment environment, TimeProvider timeProvider) : IPendingSignInKeys
{
    public const string CookieName = "PocketLedger.Identity.SignInKey";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private const byte PayloadVersion = 1;
    private const int PayloadSize = 1 + 16 + 16 + EphemeralKeyWrap.WrappingKeySize + 8;
    private readonly IDataProtector protector = protectionProvider.CreateProtector("PocketLedger.Identity.PendingSignInKey.v1");

    public void Issue(HttpContext context, UserDataKey key)
    {
        RemoveCachedKey(context);
        var id = Guid.NewGuid();
        var bindingId = id.ToString("N");
        var wrappingKey = EphemeralKeyWrap.GenerateWrappingKey();
        var payload = new byte[PayloadSize];
        try
        {
            var expiresAt = timeProvider.GetUtcNow().Add(Lifetime);
            cache.Set(CacheKey(bindingId), new Entry(key.UserId, EphemeralKeyWrap.Wrap(key, wrappingKey, EphemeralKeyPurpose.PendingSignIn, bindingId)), expiresAt);
            payload[0] = PayloadVersion;
            id.TryWriteBytes(payload.AsSpan(1, 16));
            key.UserId.TryWriteBytes(payload.AsSpan(17, 16));
            wrappingKey.CopyTo(payload, 33);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(33 + EphemeralKeyWrap.WrappingKeySize), expiresAt.ToUnixTimeSeconds());
            context.Response.Cookies.Append(CookieName, WebEncoders.Base64UrlEncode(protector.Protect(payload)), CreateCookieOptions(context, expiresAt));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public UserDataKey? TryOpen(HttpContext context, Guid userId)
    {
        var cookie = ReadCookie(context);
        if (cookie is null) return null;
        try
        {
            if (cookie.UserId != userId || !cache.TryGetValue(CacheKey(cookie.BindingId), out Entry? entry) || entry is null || entry.UserId != userId) return null;
            return EphemeralKeyWrap.Unwrap(entry.Wrapped, cookie.WrappingKey, EphemeralKeyPurpose.PendingSignIn, userId, cookie.BindingId);
        }
        catch (UserKeyException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cookie.WrappingKey);
        }
    }

    public bool Has(HttpContext context, Guid userId)
    {
        using var key = TryOpen(context, userId);
        return key is not null;
    }

    public void Clear(HttpContext context)
    {
        RemoveCachedKey(context);
        if (context.Request.Cookies.ContainsKey(CookieName)) context.Response.Cookies.Delete(CookieName, CreateCookieOptions(context, null));
    }

    private void RemoveCachedKey(HttpContext context)
    {
        var cookie = ReadCookie(context);
        if (cookie is null) return;
        cache.Remove(CacheKey(cookie.BindingId));
        CryptographicOperations.ZeroMemory(cookie.WrappingKey);
    }

    private PendingCookie? ReadCookie(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value)) return null;
        byte[] payload;
        try
        {
            payload = protector.Unprotect(WebEncoders.Base64UrlDecode(value));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return null;
        }
        try
        {
            if (payload.Length != PayloadSize || payload[0] != PayloadVersion) return null;
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(33 + EphemeralKeyWrap.WrappingKeySize)));
            if (expiresAt <= timeProvider.GetUtcNow()) return null;
            return new PendingCookie(new Guid(payload.AsSpan(1, 16)).ToString("N"), new Guid(payload.AsSpan(17, 16)), payload.AsSpan(33, EphemeralKeyWrap.WrappingKeySize).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private CookieOptions CreateCookieOptions(HttpContext context, DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = !environment.IsDevelopment() || context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        IsEssential = true,
        Expires = expiresAt
    };

    private static string CacheKey(string bindingId) => "pending-sign-in-key:" + bindingId;

    private sealed record Entry(Guid UserId, byte[] Wrapped);

    private sealed record PendingCookie(string BindingId, Guid UserId, byte[] WrappingKey);
}
