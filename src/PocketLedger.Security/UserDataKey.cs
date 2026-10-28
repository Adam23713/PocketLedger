using System.Security.Cryptography;

namespace PocketLedger.Security;

// A user's data encryption key (DEK). It is never persisted in plaintext: storage holds it only wrapped
// by a password/recovery-derived key (UserDataKeyProtection) or by a short-lived key (EphemeralKeyWrap).
public sealed class UserDataKey : IDisposable
{
    public const int Size = 32;
    private readonly byte[] key;
    private volatile bool disposed;

    internal UserDataKey(Guid userId, int keyVersion, byte[] key)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A data key must belong to a user.", nameof(userId));
        if (keyVersion < 1) throw new ArgumentOutOfRangeException(nameof(keyVersion));
        if (key.Length != Size) throw new ArgumentException("A data key must be 256 bits.", nameof(key));
        UserId = userId;
        KeyVersion = keyVersion;
        this.key = key;
    }

    public Guid UserId { get; }
    public int KeyVersion { get; }

    public static UserDataKey Generate(Guid userId, int keyVersion = 1) => new(userId, keyVersion, RandomNumberGenerator.GetBytes(Size));

    internal ReadOnlySpan<byte> Material => disposed ? throw new ObjectDisposedException(nameof(UserDataKey)) : key;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CryptographicOperations.ZeroMemory(key);
    }
}

// Authentication failures are deliberately indistinguishable: wrong secret, wrong user, wrong binding and damaged data all look the same.
public sealed class UserKeyException : CryptographicException
{
    public UserKeyException(string message) : base(message) { }
    public UserKeyException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class UserKeyUnavailableException() : InvalidOperationException("The user's data key is not available for this operation. User-encrypted data can only be read or written within an unlocked user session.");

internal static class UserKeyBinding
{
    public const int MaximumBindingLength = 128;

    public static void WriteUserId(Span<byte> destination, Guid userId)
    {
        if (userId == Guid.Empty || !userId.TryWriteBytes(destination)) throw new ArgumentException("A valid user ID is required.", nameof(userId));
    }

    public static string RequireName(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumBindingLength || value.Any(character => character is < '!' or > '~'))
            throw new ArgumentException($"The value must be 1-{MaximumBindingLength} printable ASCII characters.", parameterName);
        return value;
    }
}
