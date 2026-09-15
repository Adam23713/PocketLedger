using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PocketLedger.Services;

internal static class BackupEncryption
{
    // v1 header: magic (8), format/KDF/cipher/reserved (4), iterations (4), salt (16), nonce (12), payload length (4).
    // The complete header is authenticated as AES-GCM associated data so its metadata cannot be changed undetected.
    private const byte FormatVersion = 1;
    private const byte Pbkdf2Sha256 = 1;
    private const byte Aes256Gcm = 1;
    private const int SaltLength = 16;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private const int KeyLength = 32;
    private const int HeaderLength = 48;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes(BackupProtectionFormat.Magic);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Encrypt(string json, string password)
    {
        ValidatePassword(password);
        var plaintext = StrictUtf8.GetBytes(json);
        if (plaintext.Length > BackupProtectionFormat.MaximumPayloadBytes) throw new BusinessRuleException("The backup exceeds the 64 MiB payload limit.");

        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var header = CreateHeader(plaintext.Length, salt, nonce);
        var key = DeriveKey(password, salt, BackupProtectionFormat.Pbkdf2Iterations);
        var output = new byte[HeaderLength + plaintext.Length + TagLength];
        header.CopyTo(output, 0);
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, plaintext, output.AsSpan(HeaderLength, plaintext.Length), output.AsSpan(HeaderLength + plaintext.Length, TagLength), header);
            return output;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static byte[] Decrypt(byte[] encryptedBackup, string password)
    {
        ValidatePassword(password);
        if (encryptedBackup.Length > BackupProtectionFormat.MaximumFileBytes) throw InvalidBackup();
        if (encryptedBackup.Length < HeaderLength + TagLength) throw InvalidBackup();

        var header = encryptedBackup.AsSpan(0, HeaderLength);
        if (!header[..Magic.Length].SequenceEqual(Magic) || header[8] != FormatVersion || header[9] != Pbkdf2Sha256 || header[10] != Aes256Gcm || header[11] != 0) throw InvalidBackup();
        var iterations = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);
        if (iterations is < BackupProtectionFormat.Pbkdf2Iterations or > BackupProtectionFormat.MaximumAcceptedPbkdf2Iterations) throw InvalidBackup();
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header[44..48]);
        if (payloadLength < 0 || payloadLength > BackupProtectionFormat.MaximumPayloadBytes || encryptedBackup.Length != HeaderLength + payloadLength + TagLength) throw InvalidBackup();

        var salt = header[16..32];
        var nonce = header[32..44];
        var key = DeriveKey(password, salt, iterations);
        var plaintext = new byte[payloadLength];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(nonce, encryptedBackup.AsSpan(HeaderLength, payloadLength), encryptedBackup.AsSpan(HeaderLength + payloadLength, TagLength), plaintext, header);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw InvalidBackup();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static string DecodeJson(byte[] plaintext)
    {
        try
        {
            return StrictUtf8.GetString(plaintext);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidBackup();
        }
    }

    private static byte[] CreateHeader(int payloadLength, byte[] salt, byte[] nonce)
    {
        var header = new byte[HeaderLength];
        Magic.CopyTo(header, 0);
        header[8] = FormatVersion;
        header[9] = Pbkdf2Sha256;
        header[10] = Aes256Gcm;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), BackupProtectionFormat.Pbkdf2Iterations);
        salt.CopyTo(header, 16);
        nonce.CopyTo(header, 32);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(44, 4), payloadLength);
        return header;
    }

    private static byte[] DeriveKey(string password, ReadOnlySpan<byte> salt, int iterations)
    {
        var normalized = password.Normalize(NormalizationForm.FormC);
        return Rfc2898DeriveBytes.Pbkdf2(normalized, salt, iterations, HashAlgorithmName.SHA256, KeyLength);
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password)) throw new BusinessRuleException("Backup password is required.");
        int length;
        try { length = BackupProtectionFormat.PasswordLength(password); }
        catch (ArgumentException) { throw new BusinessRuleException("Backup password contains invalid Unicode characters."); }
        if (length is < BackupProtectionFormat.MinimumPasswordLength or > BackupProtectionFormat.MaximumPasswordLength)
            throw new BusinessRuleException($"Backup password must contain between {BackupProtectionFormat.MinimumPasswordLength} and {BackupProtectionFormat.MaximumPasswordLength} characters.");
    }

    private static BusinessRuleException InvalidBackup() => new("The backup cannot be decrypted because the password is incorrect or the file is damaged.");
}
