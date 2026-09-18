using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PocketLedger.Security;

public sealed record OciCredential(string TenancyId, string UserId, string Fingerprint, string Region, byte[] PrivateKeyPkcs8) : IDisposable
{
    public void Dispose() => CryptographicOperations.ZeroMemory(PrivateKeyPkcs8);
}

public static class OciCredentialProtection
{
    internal static ReadOnlySpan<byte> Magic => "PLOCKMS1"u8;
    internal const byte FormatVersion = 1;
    internal const byte Argon2idKdf = 1;
    internal const byte Aes256GcmCipher = 1;
    internal const int HeaderSize = 40;
    internal const int SaltSize = 16;
    internal const int NonceSize = 12;
    internal const int TagSize = 16;
    internal const int DefaultMemoryKiB = 64 * 1024;
    internal const int DefaultIterations = 3;
    internal const int DefaultParallelism = 1;
    internal const int MaximumFileBytes = 1024 * 1024;
    private const int KeySize = 32;
    private const int MaximumStringBytes = 16 * 1024;
    private const string AuthenticationError = "The OCI credential cannot be decrypted because the passphrase is incorrect or the file is damaged.";

    public static byte[] Encrypt(OciCredential credential, ReadOnlySpan<char> passphrase)
        => Encrypt(credential, passphrase, DefaultMemoryKiB, DefaultIterations, DefaultParallelism);

    internal static byte[] Encrypt(OciCredential credential, ReadOnlySpan<char> passphrase, int memoryKiB, int iterations, int parallelism)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ValidateKdfParameters(memoryKiB, iterations, parallelism);
        var plaintext = Serialize(credential);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[HeaderSize + SaltSize + NonceSize + plaintext.Length + TagSize];
        WriteHeader(output, memoryKiB, iterations, parallelism, plaintext.Length);
        salt.CopyTo(output, HeaderSize);
        nonce.CopyTo(output, HeaderSize + SaltSize);
        var key = DeriveKey(passphrase, salt, memoryKiB, iterations, parallelism);
        try
        {
            var associatedData = output.AsSpan(0, HeaderSize + SaltSize + NonceSize);
            var ciphertext = output.AsSpan(HeaderSize + SaltSize + NonceSize, plaintext.Length);
            var tag = output.AsSpan(output.Length - TagSize, TagSize);
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public static OciCredential Decrypt(ReadOnlySpan<byte> encrypted, ReadOnlySpan<char> passphrase)
    {
        try
        {
            var parameters = ParseHeader(encrypted);
            var salt = encrypted.Slice(HeaderSize, SaltSize);
            var nonce = encrypted.Slice(HeaderSize + SaltSize, NonceSize);
            var ciphertext = encrypted.Slice(HeaderSize + SaltSize + NonceSize, parameters.CiphertextLength);
            var tag = encrypted.Slice(encrypted.Length - TagSize, TagSize);
            var plaintext = new byte[parameters.CiphertextLength];
            var key = DeriveKey(passphrase, salt, parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism);
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, encrypted[..(HeaderSize + SaltSize + NonceSize)]);
                return Deserialize(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (OciCredentialException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or DecoderFallbackException or EndOfStreamException)
        {
            throw new OciCredentialException(AuthenticationError, exception);
        }
    }

    public static void WriteAtomic(string path, ReadOnlySpan<byte> encrypted, bool replaceExisting)
        => WriteAtomic(path, encrypted, replaceExisting, null);

    internal static void WriteAtomic(string path, ReadOnlySpan<byte> encrypted, bool replaceExisting, Action<string>? beforeReplace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The credential output path has no parent directory.");
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException("The credential output directory does not exist.");
        if (File.Exists(fullPath) && !replaceExisting) throw new IOException("The credential output file already exists.");
        if (File.Exists(fullPath)) EnsurePrivateFile(fullPath);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 4096, Options = FileOptions.WriteThrough };
            if (!OperatingSystem.IsWindows()) streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var stream = new FileStream(temporary, streamOptions))
            {
                stream.Write(encrypted);
                stream.Flush(flushToDisk: true);
            }
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            EnsurePrivateFile(temporary);
            beforeReplace?.Invoke(temporary);
            File.Move(temporary, fullPath, overwrite: replaceExisting);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static byte[] ReadPrivateFile(string path)
    {
        EnsurePrivateFile(path);
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaximumFileBytes) throw new OciCredentialException(AuthenticationError);
        return File.ReadAllBytes(path);
    }

    public static void ReencryptAtomic(string path, ReadOnlySpan<char> oldPassphrase, ReadOnlySpan<char> newPassphrase)
    {
        var original = ReadPrivateFile(path);
        byte[]? replacement = null;
        try
        {
            using var credential = Decrypt(original, oldPassphrase);
            replacement = Encrypt(credential, newPassphrase);
            using (Decrypt(replacement, newPassphrase)) { }
            WriteAtomic(path, replacement, replaceExisting: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(original);
            if (replacement is not null) CryptographicOperations.ZeroMemory(replacement);
        }
    }

    private static void EnsurePrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new UnauthorizedAccessException("The OCI credential file must not grant any group or other permissions (expected mode 0600 or stricter).");
        }
    }

    private static byte[] DeriveKey(ReadOnlySpan<char> passphrase, ReadOnlySpan<byte> salt, int memoryKiB, int iterations, int parallelism)
    {
        if (passphrase.IsEmpty) throw new OciCredentialException(AuthenticationError);
        var passwordBytes = new byte[Encoding.UTF8.GetByteCount(passphrase)];
        Encoding.UTF8.GetBytes(passphrase, passwordBytes);
        var saltCopy = salt.ToArray();
        try
        {
            using var argon2 = new Argon2id(passwordBytes) { Salt = saltCopy, MemorySize = memoryKiB, Iterations = iterations, DegreeOfParallelism = parallelism };
            return argon2.GetBytes(KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(saltCopy);
        }
    }

    private static byte[] Serialize(OciCredential credential)
    {
        var values = new[] { credential.TenancyId, credential.UserId, credential.Fingerprint, credential.Region };
        var encoded = values.Select(value => Encoding.UTF8.GetBytes(value.Trim())).ToArray();
        try
        {
            if (encoded.Any(value => value.Length is <= 0 or > MaximumStringBytes) || credential.PrivateKeyPkcs8.Length is < 256 or > MaximumFileBytes)
                throw new ArgumentException("The OCI credential contains an invalid field length.");
            var length = checked(1 + encoded.Sum(value => 4 + value.Length) + 4 + credential.PrivateKeyPkcs8.Length);
            if (length > MaximumFileBytes) throw new ArgumentException("The OCI credential is too large.");
            var plaintext = new byte[length];
            var offset = 0;
            plaintext[offset++] = 1;
            foreach (var value in encoded)
            {
                BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset), value.Length);
                offset += 4;
                value.CopyTo(plaintext, offset);
                offset += value.Length;
            }
            BinaryPrimitives.WriteInt32LittleEndian(plaintext.AsSpan(offset), credential.PrivateKeyPkcs8.Length);
            offset += 4;
            credential.PrivateKeyPkcs8.CopyTo(plaintext, offset);
            return plaintext;
        }
        finally
        {
            foreach (var value in encoded) CryptographicOperations.ZeroMemory(value);
        }
    }

    private static OciCredential Deserialize(byte[] plaintext)
    {
        using var stream = new MemoryStream(plaintext, writable: false);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        if (reader.ReadByte() != 1) throw new OciCredentialException(AuthenticationError);
        var tenancy = ReadString(reader);
        var user = ReadString(reader);
        var fingerprint = ReadString(reader);
        var region = ReadString(reader);
        var keyLength = reader.ReadInt32();
        if (keyLength is < 256 or > MaximumFileBytes || keyLength > stream.Length - stream.Position) throw new OciCredentialException(AuthenticationError);
        var key = reader.ReadBytes(keyLength);
        if (key.Length != keyLength || stream.Position != stream.Length) { CryptographicOperations.ZeroMemory(key); throw new OciCredentialException(AuthenticationError); }
        return new OciCredential(tenancy, user, fingerprint, region, key);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is <= 0 or > MaximumStringBytes || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new OciCredentialException(AuthenticationError);
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteHeader(Span<byte> output, int memoryKiB, int iterations, int parallelism, int ciphertextLength)
    {
        Magic.CopyTo(output);
        output[8] = FormatVersion;
        output[9] = Argon2idKdf;
        output[10] = Aes256GcmCipher;
        output[11] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(output[12..], memoryKiB);
        BinaryPrimitives.WriteInt32LittleEndian(output[16..], iterations);
        BinaryPrimitives.WriteInt32LittleEndian(output[20..], parallelism);
        output[24] = SaltSize;
        output[25] = NonceSize;
        output[26] = TagSize;
        BinaryPrimitives.WriteInt32LittleEndian(output[28..], ciphertextLength);
    }

    private static KdfParameters ParseHeader(ReadOnlySpan<byte> encrypted)
    {
        if (encrypted.Length < HeaderSize + SaltSize + NonceSize + TagSize || encrypted.Length > MaximumFileBytes || !encrypted[..8].SequenceEqual(Magic)) throw new OciCredentialException(AuthenticationError);
        if (encrypted[8] != FormatVersion) throw new OciCredentialException("The OCI credential format version is not supported.");
        if (encrypted[9] != Argon2idKdf || encrypted[10] != Aes256GcmCipher || encrypted[11] != 0 || encrypted[24] != SaltSize || encrypted[25] != NonceSize || encrypted[26] != TagSize)
            throw new OciCredentialException(AuthenticationError);
        var parameters = new KdfParameters(BinaryPrimitives.ReadInt32LittleEndian(encrypted[12..]), BinaryPrimitives.ReadInt32LittleEndian(encrypted[16..]), BinaryPrimitives.ReadInt32LittleEndian(encrypted[20..]), BinaryPrimitives.ReadInt32LittleEndian(encrypted[28..]));
        ValidateKdfParameters(parameters.MemoryKiB, parameters.Iterations, parameters.Parallelism);
        if (parameters.CiphertextLength <= 0 || encrypted.Length != HeaderSize + SaltSize + NonceSize + parameters.CiphertextLength + TagSize) throw new OciCredentialException(AuthenticationError);
        return parameters;
    }

    private static void ValidateKdfParameters(int memoryKiB, int iterations, int parallelism)
    {
        if (memoryKiB is < 32 * 1024 or > 256 * 1024 || iterations is < 2 or > 10 || parallelism is < 1 or > 8)
            throw new OciCredentialException("The OCI credential contains unsafe or unsupported KDF parameters.");
    }

    private sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism, int CiphertextLength);
}

public sealed class OciCredentialException : CryptographicException
{
    public OciCredentialException(string message) : base(message) { }
    public OciCredentialException(string message, Exception innerException) : base(message, innerException) { }
}
