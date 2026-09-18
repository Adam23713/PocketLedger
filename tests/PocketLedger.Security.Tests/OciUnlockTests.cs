using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Oci.KeymanagementService.Models;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class OciUnlockTests
{
    [Fact]
    public void StartupState_IsLockedAndProviderDoesNotFallBackToLocal()
    {
        var (provider, state, path) = CreateProvider(new FakeKmsClient());
        try
        {
            Assert.False(state.IsReady);
            Assert.Equal(EncryptionProviderNames.OciVault, provider.Name);
            Assert.Throws<CryptographicException>(() => provider.Encrypt(new XElement("key")));
        }
        finally { provider.Dispose(); File.Delete(path); }
    }

    [Fact]
    public async Task SuccessfulUnlock_VerifiesKmsAndMarksReady()
    {
        var (provider, state, path) = CreateProvider(new FakeKmsClient());
        try
        {
            await provider.UnlockAsync("test passphrase".ToCharArray(), CancellationToken.None);
            Assert.True(state.IsReady);
            var wrapped = provider.Encrypt(new XElement("key", "value"));
            Assert.Equal("key", provider.Decrypt(wrapped.EncryptedElement).Name.LocalName);
        }
        finally { provider.Dispose(); File.Delete(path); }
    }

    [Fact]
    public async Task FailedUnlock_RemainsLocked()
    {
        var (provider, state, path) = CreateProvider(new FakeKmsClient(fail: true));
        try
        {
            await Assert.ThrowsAsync<CryptographicException>(() => provider.UnlockAsync("test passphrase".ToCharArray(), CancellationToken.None));
            Assert.False(state.IsReady);
            Assert.Throws<CryptographicException>(() => provider.Encrypt(new XElement("key")));
        }
        finally { provider.Dispose(); File.Delete(path); }
    }

    [Fact]
    public async Task WrongPassphrase_RemainsLocked()
    {
        var (provider, state, path) = CreateProvider(new FakeKmsClient());
        try
        {
            await Assert.ThrowsAsync<OciCredentialException>(() => provider.UnlockAsync("wrong".ToCharArray(), CancellationToken.None));
            Assert.False(state.IsReady);
        }
        finally { provider.Dispose(); File.Delete(path); }
    }

    private static (LockedOciKeyEncryptionProvider Provider, EncryptionRuntimeState State, string Path) CreateProvider(FakeKmsClient client)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pocketledger-{Guid.NewGuid():N}.enc");
        using var credential = new OciCredential("ocid1.tenancy.oc1..test", "ocid1.user.oc1..test", "aa:bb", "eu-frankfurt-1", RandomNumberGenerator.GetBytes(512));
        var encrypted = OciCredentialProtection.Encrypt(credential, "test passphrase", 32 * 1024, 2, 1);
        OciCredentialProtection.WriteAtomic(path, encrypted, replaceExisting: false);
        var state = new EncryptionRuntimeState(requiresUnlock: true);
        var options = new OciVaultOptions("ocid1.key.oc1..test", "https://example.invalid", path, "/tmp/test.sock", 1, 1);
        var provider = new LockedOciKeyEncryptionProvider(options, state, (_, _) => new OciKmsKeyEncryptionProvider(options.KeyId, client));
        return (provider, state, path);
    }

    private sealed class FakeKmsClient(bool fail = false) : IOciKmsClient
    {
        public EncryptedData Encrypt(EncryptDataDetails details)
        {
            if (fail) throw new CryptographicException("simulated KMS failure");
            return new EncryptedData { Ciphertext = Convert.ToBase64String(Encoding.UTF8.GetBytes(details.Plaintext)), KeyId = details.KeyId, KeyVersionId = "version" };
        }

        public DecryptedData Decrypt(DecryptDataDetails details)
        {
            if (fail) throw new CryptographicException("simulated KMS failure");
            return new DecryptedData { Plaintext = Encoding.UTF8.GetString(Convert.FromBase64String(details.Ciphertext)) };
        }

        public void Dispose() { }
    }
}
