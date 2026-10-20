using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PocketLedger.Security;

// Inner, per-user layer for database fields and cache payloads. Layout: version, key version, nonce, AES-256-GCM(plaintext), tag.
// The field group (e.g. "Note", "Name") and the user ID are associated data. Groups are shared across entities on purpose,
// so background workers can copy a template's ciphertext into a generated row without the user's key.
public static class UserFieldCipher
{
    private static ReadOnlySpan<byte> Context => "PocketLedger.UserField.v1"u8;
    internal const byte FormatVersion = 1;
    internal const int HeaderSize = 5;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int Overhead = HeaderSize + NonceSize + TagSize;
    private const string AuthenticationError = "The user-encrypted value cannot be decrypted with this user's data key.";

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, UserDataKey key, string fieldGroup)
    {
        ArgumentNullException.ThrowIfNull(key);
        UserKeyBinding.RequireName(fieldGroup, nameof(fieldGroup));
        var output = new byte[checked(Overhead + plaintext.Length)];
        output[0] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(1), key.KeyVersion);
        RandomNumberGenerator.Fill(output.AsSpan(HeaderSize, NonceSize));
        using var aes = new AesGcm(key.Material, TagSize);
        aes.Encrypt(output.AsSpan(HeaderSize, NonceSize), plaintext, output.AsSpan(HeaderSize + NonceSize, plaintext.Length), output.AsSpan(output.Length - TagSize), AssociatedData(output, key.UserId, fieldGroup));
        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext, UserDataKey key, string fieldGroup)
    {
        ArgumentNullException.ThrowIfNull(key);
        UserKeyBinding.RequireName(fieldGroup, nameof(fieldGroup));
        if (ciphertext.Length < Overhead) throw new UserKeyException(AuthenticationError);
        if (ciphertext[0] != FormatVersion) throw new UserKeyException("The user-encrypted value format version is not supported.");
        if (BinaryPrimitives.ReadInt32LittleEndian(ciphertext[1..]) != key.KeyVersion) throw new UserKeyException("The user-encrypted value was written with a different data key version.");
        var plaintext = new byte[ciphertext.Length - Overhead];
        try
        {
            using var aes = new AesGcm(key.Material, TagSize);
            aes.Decrypt(ciphertext.Slice(HeaderSize, NonceSize), ciphertext.Slice(HeaderSize + NonceSize, plaintext.Length), ciphertext[^TagSize..], plaintext, AssociatedData(ciphertext, key.UserId, fieldGroup));
            return plaintext;
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new UserKeyException(AuthenticationError, exception);
        }
    }

    public static string EncryptString(string plaintext, UserDataKey key, string fieldGroup)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return Convert.ToBase64String(Encrypt(bytes, key, fieldGroup));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public static string DecryptString(string ciphertext, UserDataKey key, string fieldGroup)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        byte[] encrypted;
        try
        {
            encrypted = Convert.FromBase64String(ciphertext);
        }
        catch (FormatException exception)
        {
            throw new UserKeyException(AuthenticationError, exception);
        }
        var plaintext = Decrypt(encrypted, key, fieldGroup);
        try
        {
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        catch (DecoderFallbackException exception)
        {
            throw new UserKeyException(AuthenticationError, exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(ReadOnlySpan<byte> value, Guid userId, string fieldGroup)
    {
        var group = Encoding.ASCII.GetBytes(fieldGroup);
        var associatedData = new byte[Context.Length + 16 + 1 + group.Length + HeaderSize];
        Context.CopyTo(associatedData);
        UserKeyBinding.WriteUserId(associatedData.AsSpan(Context.Length, 16), userId);
        associatedData[Context.Length + 16] = (byte)group.Length;
        group.CopyTo(associatedData, Context.Length + 17);
        value[..HeaderSize].CopyTo(associatedData.AsSpan(Context.Length + 17 + group.Length));
        return associatedData;
    }
}
