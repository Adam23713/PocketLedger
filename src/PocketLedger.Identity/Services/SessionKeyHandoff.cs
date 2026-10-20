using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using PocketLedger.Security;

namespace PocketLedger.Services;

public sealed record SessionKeyMaterial(string SessionId, string SessionKey, string WrappedKey);

public interface ISessionKeyHandoff
{
    // Prepares SK and W for a new signed-in session; returns the handoff ID carried by the authorization code.
    (string HandoffId, string SessionId) Prepare(UserDataKey key);
    // Single use: the material is removed whether or not it matches.
    SessionKeyMaterial? Take(string handoffId, Guid userId, string sessionId);
}

// Bridges the browser-facing authorization request and the server-to-server code exchange. SK and W stay in Identity's memory
// only for the seconds between the two and are never written to the authorization code or any database.
public sealed class SessionKeyHandoff(IMemoryCache cache) : ISessionKeyHandoff
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public (string HandoffId, string SessionId) Prepare(UserDataKey key)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var handoffId = Guid.NewGuid().ToString("N");
        var sessionKey = EphemeralKeyWrap.GenerateWrappingKey();
        try
        {
            var wrapped = EphemeralKeyWrap.Wrap(key, sessionKey, EphemeralKeyPurpose.Session, sessionId);
            var entry = new Entry(key.UserId, sessionId, UserSessionKeyProtocol.Encode(sessionKey), UserSessionKeyProtocol.Encode(wrapped));
            cache.Set(CacheKey(handoffId), entry, Lifetime);
            return (handoffId, sessionId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }

    public SessionKeyMaterial? Take(string handoffId, Guid userId, string sessionId)
    {
        var cacheKey = CacheKey(handoffId);
        if (!cache.TryGetValue(cacheKey, out Entry? entry) || entry is null) return null;
        cache.Remove(cacheKey);
        return entry.UserId == userId && entry.SessionId == sessionId ? new SessionKeyMaterial(entry.SessionId, entry.SessionKey, entry.WrappedKey) : null;
    }

    private static string CacheKey(string handoffId) => "session-key-handoff:" + handoffId;

    private sealed record Entry(Guid UserId, string SessionId, string SessionKey, string WrappedKey);
}
