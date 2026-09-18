using System.Buffers.Binary;
using System.Security.Cryptography;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class OciCredentialProtectionTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public void EncryptDecrypt_RoundTripsWithVersionedAuthenticatedFormat()
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        using var decrypted = OciCredentialProtection.Decrypt(encrypted, Password);

        Assert.True(encrypted.AsSpan(0, 8).SequenceEqual("PLOCKMS1"u8));
        Assert.Equal(1, encrypted[8]);
        Assert.Equal(credential.TenancyId, decrypted.TenancyId);
        Assert.Equal(credential.UserId, decrypted.UserId);
        Assert.Equal(credential.Fingerprint, decrypted.Fingerprint);
        Assert.Equal(credential.Region, decrypted.Region);
        Assert.Equal(credential.PrivateKeyPkcs8, decrypted.PrivateKeyPkcs8);
    }

    [Fact]
    public void WrongPassphrase_IsRejected()
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted, "wrong password"));
    }

    [Theory]
    [InlineData(80)]
    [InlineData(-1)]
    public void CorruptedCiphertextOrTag_IsRejected(int offset)
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        var actualOffset = offset < 0 ? encrypted.Length - 1 : offset;
        encrypted[actualOffset] ^= 1;
        Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted, Password));
    }

    [Fact]
    public void UnsupportedVersion_IsReported()
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        encrypted[8] = 99;
        var exception = Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted, Password));
        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidKdfParameters_AreRejectedBeforeDerivation()
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        BinaryPrimitives.WriteInt32LittleEndian(encrypted.AsSpan(12), int.MaxValue);
        var exception = Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted, Password));
        Assert.Contains("KDF", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncatedCredential_IsRejected()
    {
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted[..^5], Password));
    }

    [Fact]
    public void ReencryptAtomic_ChangesPassphraseAndPreservesCredential()
    {
        var path = CreateCredentialFile();
        try
        {
            OciCredentialProtection.ReencryptAtomic(path, Password, "new strong passphrase");
            var encrypted = OciCredentialProtection.ReadPrivateFile(path);
            Assert.Throws<OciCredentialException>(() => OciCredentialProtection.Decrypt(encrypted, Password));
            using var decrypted = OciCredentialProtection.Decrypt(encrypted, "new strong passphrase");
            Assert.Equal("ocid1.tenancy.oc1..test", decrypted.TenancyId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FailedReencrypt_DoesNotDestroyOriginal()
    {
        var path = CreateCredentialFile();
        try
        {
            var original = File.ReadAllBytes(path);
            Assert.Throws<OciCredentialException>(() => OciCredentialProtection.ReencryptAtomic(path, "wrong", "replacement"));
            Assert.Equal(original, File.ReadAllBytes(path));
            using var decrypted = OciCredentialProtection.Decrypt(original, Password);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InterruptedAtomicReplacement_DoesNotDestroyOriginal()
    {
        var path = CreateCredentialFile();
        try
        {
            var original = File.ReadAllBytes(path);
            using var credential = CreateCredential();
            var replacement = EncryptForTest(credential, "replacement passphrase");

            Assert.Throws<IOException>(() => OciCredentialProtection.WriteAtomic(path, replacement, replaceExisting: true, _ => throw new IOException("simulated interruption")));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.*.tmp"));
        }
        finally { File.Delete(path); }
    }

    private static byte[] EncryptForTest(OciCredential credential, ReadOnlySpan<char> password)
        => OciCredentialProtection.Encrypt(credential, password, 32 * 1024, 2, 1);

    private static OciCredential CreateCredential() => new("ocid1.tenancy.oc1..test", "ocid1.user.oc1..test", "aa:bb:cc", "eu-frankfurt-1", RandomNumberGenerator.GetBytes(512));

    private static string CreateCredentialFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pocketledger-{Guid.NewGuid():N}.enc");
        using var credential = CreateCredential();
        var encrypted = EncryptForTest(credential, Password);
        OciCredentialProtection.WriteAtomic(path, encrypted, replaceExisting: false);
        return path;
    }
}
