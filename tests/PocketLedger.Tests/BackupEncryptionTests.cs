using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PocketLedger.Services;

namespace PocketLedger.Tests;

public class BackupEncryptionTests
{
    private const string Password = "correct-horse-battery-staple";
    private const string AuthenticationError = "The backup cannot be decrypted because the password is incorrect or the file is damaged.";

    [Fact]
    public void EncryptedBackup_RoundTripsUnicodeJsonAndUsesVersionedHeader()
    {
        const string json = "{\"version\":4,\"note\":\"árvíztűrő tükörfúrógép\"}";

        var encrypted = BackupEncryption.Encrypt(json, Password);
        var plaintext = BackupEncryption.Decrypt(encrypted, Password);
        try
        {
            Assert.Equal(json, BackupEncryption.DecodeJson(plaintext));
            Assert.Equal(Encoding.UTF8.GetByteCount(json) + 64, encrypted.Length);
            Assert.True(encrypted.AsSpan(0, 8).SequenceEqual("PLBACKUP"u8));
            Assert.Equal(1, encrypted[8]);
            Assert.Equal(1, encrypted[9]);
            Assert.Equal(1, encrypted[10]);
            Assert.Equal(0, encrypted[11]);
            Assert.Equal(BackupProtectionFormat.Pbkdf2Iterations, BinaryPrimitives.ReadInt32LittleEndian(encrypted.AsSpan(12, 4)));
            Assert.Equal(Encoding.UTF8.GetByteCount(json), BinaryPrimitives.ReadInt32LittleEndian(encrypted.AsSpan(44, 4)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [Fact]
    public void RepeatedEncryption_UsesDifferentSaltAndNonce()
    {
        var first = BackupEncryption.Encrypt("{}", Password);
        var second = BackupEncryption.Encrypt("{}", Password);

        Assert.False(first.AsSpan(16, 16).SequenceEqual(second.AsSpan(16, 16)));
        Assert.False(first.AsSpan(32, 12).SequenceEqual(second.AsSpan(32, 12)));
        Assert.False(first.SequenceEqual(second));
    }

    [Fact]
    public void WrongPasswordOrModifiedBackup_IsRejectedWithTheSameError()
    {
        var encrypted = BackupEncryption.Encrypt("{\"version\":4}", Password);
        AssertAuthenticationFailure(() => BackupEncryption.Decrypt(encrypted, "incorrect-password"));

        foreach (var offset in new[] { 8, 16, 32, 48, encrypted.Length - 1 })
        {
            var modified = encrypted.ToArray();
            modified[offset] ^= 1;
            AssertAuthenticationFailure(() => BackupEncryption.Decrypt(modified, Password));
        }
    }

    [Fact]
    public void UntrustedKdfParametersOutsideTheAcceptedRange_AreRejectedBeforeDecryption()
    {
        var encrypted = BackupEncryption.Encrypt("{}", Password);
        foreach (var iterations in new[] { BackupProtectionFormat.Pbkdf2Iterations - 1, BackupProtectionFormat.MaximumAcceptedPbkdf2Iterations + 1 })
        {
            var modified = encrypted.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(modified.AsSpan(12, 4), iterations);
            AssertAuthenticationFailure(() => BackupEncryption.Decrypt(modified, Password));
        }
    }

    [Fact]
    public void Passwords_AreValidatedByNormalizedUnicodeLength()
    {
        Assert.Throws<BusinessRuleException>(() => BackupEncryption.Encrypt("{}", "123456789"));
        Assert.Throws<BusinessRuleException>(() => BackupEncryption.Encrypt("{}", new string('x', BackupProtectionFormat.MaximumPasswordLength + 1)));

        const string composed = "á123456789";
        const string decomposed = "a\u0301123456789";
        var encrypted = BackupEncryption.Encrypt("{}", composed);
        var plaintext = BackupEncryption.Decrypt(encrypted, decomposed);
        try { Assert.Equal("{}", BackupEncryption.DecodeJson(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    [Fact]
    public void EncryptedPreview_DecryptsBeforeRunningExistingSemanticValidation()
    {
        var validPayload = BackupJson.Serialize(new PocketLedgerBackup(4, DateTimeOffset.UtcNow, [], [], [], []));
        var invalidPayload = BackupJson.Serialize(new PocketLedgerBackup(999, DateTimeOffset.UtcNow, [], [], [], []));
        var service = new ImportExportService(null!, null!, null!);

        var validPreview = service.PreviewEncryptedRestore(BackupEncryption.Encrypt(validPayload, Password), Password);
        var invalidPreview = service.PreviewEncryptedRestore(BackupEncryption.Encrypt(invalidPayload, Password), Password);

        Assert.True(validPreview.IsValid);
        Assert.False(invalidPreview.IsValid);
        Assert.Contains(invalidPreview.Errors, error => error.Contains("Unsupported backup version"));
    }

    [Fact]
    public async Task RestoreWithWrongPassword_FailsBeforeAccessingPersistence()
    {
        var encrypted = BackupEncryption.Encrypt("{}", Password);
        var serviceWithoutPersistence = new ImportExportService(null!, null!, null!);

        var exception = await Assert.ThrowsAsync<BusinessRuleException>(() => serviceWithoutPersistence.RestoreEncryptedAsync(encrypted, "incorrect-password", CancellationToken.None));

        Assert.Equal(AuthenticationError, exception.Message);
    }

    [Fact]
    public void MalformedLengthAndOversizedFiles_AreRejectedBeforeKeyDerivation()
    {
        var encrypted = BackupEncryption.Encrypt("{}", Password);
        BinaryPrimitives.WriteInt32LittleEndian(encrypted.AsSpan(44, 4), int.MaxValue);
        AssertAuthenticationFailure(() => BackupEncryption.Decrypt(encrypted, Password));

        var oversized = new byte[BackupProtectionFormat.MaximumFileBytes + 1];
        AssertAuthenticationFailure(() => BackupEncryption.Decrypt(oversized, Password));
    }

    private static void AssertAuthenticationFailure(Action action)
    {
        var exception = Assert.Throws<BusinessRuleException>(action);
        Assert.Equal(AuthenticationError, exception.Message);
    }
}
