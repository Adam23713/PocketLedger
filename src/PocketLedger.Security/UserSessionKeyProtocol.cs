using Microsoft.AspNetCore.WebUtilities;

namespace PocketLedger.Security;

// How a signed-in session's data key travels: Identity hands SK and W = AES-GCM(SK, DEK) to the Web BFF once, in the
// authorization-code token response. The Web BFF keeps W in its server-side ticket and SK only in a browser cookie, and
// sends both to the API with every call. The API unwraps the DEK for that request only.
public static class UserSessionKeyProtocol
{
    // Access-token claim binding W to one sign-in; copied into every refreshed access token.
    public const string SessionIdClaim = "pl_sid";
    // Authorization-code-only claim that lets the token endpoint pick up the session material prepared at authorization.
    public const string HandoffClaim = "pl_session_handoff";
    public const string SessionKeyParameter = "pl_session_key";
    public const string WrappedKeyParameter = "pl_wrapped_data_key";
    public const string SessionKeyHeader = "X-PocketLedger-Session-Key";
    public const string WrappedKeyHeader = "X-PocketLedger-Wrapped-Data-Key";

    public static string Encode(ReadOnlySpan<byte> value) => WebEncoders.Base64UrlEncode(value);

    public static byte[]? TryDecode(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 256) return null;
        try
        {
            return WebEncoders.Base64UrlDecode(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Unwraps the DEK presented with a request, or returns null when anything does not match.
    public static UserDataKey? TryUnwrap(string? sessionKey, string? wrappedKey, Guid userId, string? sessionId)
    {
        var key = TryDecode(sessionKey);
        var wrapped = TryDecode(wrappedKey);
        try
        {
            if (key is null || wrapped is null || userId == Guid.Empty || string.IsNullOrEmpty(sessionId)) return null;
            return EphemeralKeyWrap.Unwrap(wrapped, key, EphemeralKeyPurpose.Session, userId, sessionId);
        }
        catch (UserKeyException)
        {
            return null;
        }
        finally
        {
            if (key is not null) System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }
}
