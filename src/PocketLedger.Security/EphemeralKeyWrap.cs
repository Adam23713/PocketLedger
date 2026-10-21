using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PocketLedger.Security;

public enum EphemeralKeyPurpose : byte
{
    // From the password (or data password) step until sign-in completes, including TOTP and authenticator setup;
    // the wrapping key lives only in the pending sign-in cookie.
    PendingSignIn = 1,
    // For a signed-in session; the wrapping key (SK) lives only in the browser cookie.
    Session = 2,
}

// Wraps a DEK with a random, short-lived key that the server does not keep. Layout: version, purpose, key version, nonce, AES-256-GCM(DEK), tag.
// Purpose, user ID and binding ID (session or pending-login ID) are associated data, so a wrapped key only opens in the context it was issued for.
public static class EphemeralKeyWrap
{
    public const int WrappingKeySize = 32;
    private static ReadOnlySpan<byte> Context => "PocketLedger.EphemeralUserKey.v1"u8;
    internal const byte FormatVersion = 1;
    internal const int HeaderSize = 6;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int WrappedSize = HeaderSize + NonceSize + UserDataKey.Size + TagSize;
    private const string AuthenticationError = "The session data key cannot be unlocked.";

    public static byte[] GenerateWrappingKey() => RandomNumberGenerator.GetBytes(WrappingKeySize);

    public static byte[] Wrap(UserDataKey key, ReadOnlySpan<byte> wrappingKey, EphemeralKeyPurpose purpose, string bindingId)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (wrappingKey.Length != WrappingKeySize) throw new ArgumentException("The wrapping key must be 256 bits.", nameof(wrappingKey));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        UserKeyBinding.RequireName(bindingId, nameof(bindingId));
        var output = new byte[WrappedSize];
        output[0] = FormatVersion;
        output[1] = (byte)purpose;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(2), key.KeyVersion);
        RandomNumberGenerator.Fill(output.AsSpan(HeaderSize, NonceSize));
        using var aes = new AesGcm(wrappingKey, TagSize);
        aes.Encrypt(output.AsSpan(HeaderSize, NonceSize), key.Material, output.AsSpan(HeaderSize + NonceSize, UserDataKey.Size), output.AsSpan(WrappedSize - TagSize), AssociatedData(output, key.UserId, bindingId));
        return output;
    }

    public static UserDataKey Unwrap(ReadOnlySpan<byte> wrapped, ReadOnlySpan<byte> wrappingKey, EphemeralKeyPurpose purpose, Guid userId, string bindingId)
    {
        if (wrappingKey.Length != WrappingKeySize || wrapped.Length != WrappedSize || wrapped[1] != (byte)purpose) throw new UserKeyException(AuthenticationError);
        if (wrapped[0] != FormatVersion) throw new UserKeyException("The session data key format version is not supported.");
        var keyVersion = BinaryPrimitives.ReadInt32LittleEndian(wrapped[2..]);
        if (keyVersion < 1) throw new UserKeyException(AuthenticationError);
        byte[] associatedData;
        try
        {
            associatedData = AssociatedData(wrapped, userId, UserKeyBinding.RequireName(bindingId, nameof(bindingId)));
        }
        catch (ArgumentException exception)
        {
            throw new UserKeyException(AuthenticationError, exception);
        }
        var material = new byte[UserDataKey.Size];
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(wrapped.Slice(HeaderSize, NonceSize), wrapped.Slice(HeaderSize + NonceSize, UserDataKey.Size), wrapped[^TagSize..], material, associatedData);
            return new UserDataKey(userId, keyVersion, material);
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(material);
            throw new UserKeyException(AuthenticationError, exception);
        }
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> wrapped, Guid userId, string bindingId)
    {
        var binding = Encoding.ASCII.GetBytes(bindingId);
        var associatedData = new byte[Context.Length + 16 + 1 + binding.Length + HeaderSize];
        Context.CopyTo(associatedData);
        UserKeyBinding.WriteUserId(associatedData.AsSpan(Context.Length, 16), userId);
        associatedData[Context.Length + 16] = (byte)binding.Length;
        binding.CopyTo(associatedData, Context.Length + 17);
        wrapped[..HeaderSize].CopyTo(associatedData.AsSpan(Context.Length + 17 + binding.Length));
        return associatedData;
    }
}
