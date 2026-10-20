using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PocketLedger.Security;

public enum UserKeySecretKind : byte
{
    Password = 1,
    RecoveryKey = 2,
}

// Wraps a DEK with a key derived from the user's password (or data password) or recovery key.
// Format "PLUDEK01": authenticated header with KDF parameters and key version, salt, nonce, AES-256-GCM(DEK), tag.
// The user ID is bound as associated data, so a wrapped key copied to another user's row cannot be unlocked there.
public static class UserDataKeyProtection
{
    internal static ReadOnlySpan<byte> Magic => "PLUDEK01"u8;
    private static ReadOnlySpan<byte> Context => "PocketLedger.UserDataKey.v1"u8;
    internal const byte FormatVersion = 1;
    internal const byte Argon2idKdf = 1;
    internal const byte Aes256GcmCipher = 1;
    internal const int HeaderSize = 32;
    internal const int SaltSize = 16;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int WrappedSize = HeaderSize + SaltSize + NonceSize + UserDataKey.Size + TagSize;
    internal const int DefaultMemoryKiB = 64 * 1024;
    internal const int DefaultIterations = 3;
    internal const int DefaultParallelism = 1;
    private const int AuthenticatedPrefixSize = HeaderSize + SaltSize + NonceSize;
    private const string AuthenticationError = "The data key cannot be unlocked because the secret is incorrect or the stored key is damaged.";

    public static byte[] Wrap(UserDataKey key, ReadOnlySpan<char> secret, UserKeySecretKind kind)
        => Wrap(key, secret, kind, DefaultMemoryKiB, DefaultIterations, DefaultParallelism);

    internal static byte[] Wrap(UserDataKey key, ReadOnlySpan<char> secret, UserKeySecretKind kind, int memoryKiB, int iterations, int parallelism)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Argon2idKeyDerivation.AreParametersSupported(memoryKiB, iterations, parallelism)) throw new ArgumentOutOfRangeException(nameof(memoryKiB), "Unsupported Argon2id parameters.");
        var normalizedSecret = NormalizeSecret(secret, kind) ?? throw new ArgumentException("The secret is empty or not a valid recovery key.", nameof(secret));
        var output = new byte[WrappedSize];
        WriteHeader(output, kind, key.KeyVersion, memoryKiB, iterations, parallelism);
        RandomNumberGenerator.Fill(output.AsSpan(HeaderSize, SaltSize + NonceSize));
        var wrappingKey = Argon2idKeyDerivation.DeriveKey(normalizedSecret, output.AsSpan(HeaderSize, SaltSize), memoryKiB, iterations, parallelism);
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Encrypt(output.AsSpan(HeaderSize + SaltSize, NonceSize), key.Material, output.AsSpan(AuthenticatedPrefixSize, UserDataKey.Size), output.AsSpan(WrappedSize - TagSize), AssociatedData(output, key.UserId));
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    public static UserDataKey Unwrap(ReadOnlySpan<byte> wrapped, ReadOnlySpan<char> secret, Guid userId, UserKeySecretKind expectedKind)
    {
        var parameters = ParseHeader(wrapped, expectedKind);
        var normalizedSecret = NormalizeSecret(secret, expectedKind) ?? throw new UserKeyException(AuthenticationError);
        var associatedData = AssociatedData(wrapped, userId);
        var wrappingKey = Argon2idKeyDerivation.DeriveKey(normalizedSecret, wrapped.Slice(HeaderSize, SaltSize), parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism);
        var material = new byte[UserDataKey.Size];
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(wrapped.Slice(HeaderSize + SaltSize, NonceSize), wrapped.Slice(AuthenticatedPrefixSize, UserDataKey.Size), wrapped[^TagSize..], material, associatedData);
            return new UserDataKey(userId, parameters.KeyVersion, material);
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(material);
            throw new UserKeyException(AuthenticationError, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private static string? NormalizeSecret(ReadOnlySpan<char> secret, UserKeySecretKind kind)
    {
        if (kind == UserKeySecretKind.RecoveryKey) return RecoveryKey.TryNormalize(secret, out var normalized) ? normalized : null;
        return secret.IsEmpty ? null : secret.ToString();
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> wrapped, Guid userId)
    {
        var associatedData = new byte[Context.Length + 16 + AuthenticatedPrefixSize];
        Context.CopyTo(associatedData);
        UserKeyBinding.WriteUserId(associatedData.AsSpan(Context.Length, 16), userId);
        wrapped[..AuthenticatedPrefixSize].CopyTo(associatedData.AsSpan(Context.Length + 16));
        return associatedData;
    }

    private static void WriteHeader(Span<byte> output, UserKeySecretKind kind, int keyVersion, int memoryKiB, int iterations, int parallelism)
    {
        Magic.CopyTo(output);
        output[8] = FormatVersion;
        output[9] = (byte)kind;
        output[10] = Argon2idKdf;
        output[11] = Aes256GcmCipher;
        BinaryPrimitives.WriteInt32LittleEndian(output[12..], memoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(output[16..], iterations);
        BinaryPrimitives.WriteInt32LittleEndian(output[20..], parallelism);
        BinaryPrimitives.WriteInt32LittleEndian(output[24..], keyVersion);
        output[28] = SaltSize;
        output[29] = NonceSize;
        output[30] = TagSize;
        output[31] = 0;
    }

    private static WrapParameters ParseHeader(ReadOnlySpan<byte> wrapped, UserKeySecretKind expectedKind)
    {
        if (wrapped.Length != WrappedSize || !wrapped[..8].SequenceEqual(Magic)) throw new UserKeyException(AuthenticationError);
        if (wrapped[8] != FormatVersion) throw new UserKeyException("The wrapped data key format version is not supported.");
        if (wrapped[9] != (byte)expectedKind || wrapped[10] != Argon2idKdf || wrapped[11] != Aes256GcmCipher || wrapped[28] != SaltSize || wrapped[29] != NonceSize || wrapped[30] != TagSize || wrapped[31] != 0)
            throw new UserKeyException(AuthenticationError);
        var parameters = new WrapParameters(BinaryPrimitives.ReadInt32LittleEndian(wrapped[12..]), BinaryPrimitives.ReadInt32LittleEndian(wrapped[16..]), BinaryPrimitives.ReadInt32LittleEndian(wrapped[20..]), BinaryPrimitives.ReadInt32LittleEndian(wrapped[24..]));
        if (!Argon2idKeyDerivation.AreParametersSupported(parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism))
            throw new UserKeyException("The wrapped data key contains unsafe or unsupported KDF parameters.");
        if (parameters.KeyVersion < 1) throw new UserKeyException(AuthenticationError);
        return parameters;
    }

    private sealed record WrapParameters(int MemoryKiB, int Iterations, int Parallelism, int KeyVersion);
}
