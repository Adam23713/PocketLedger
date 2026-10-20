using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using PocketLedger.Security;

namespace PocketLedger.Security.Tests;

public sealed class UserDataKeyProtectionTests
{
    private const string Password = "correct horse battery staple";
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void PasswordWrap_RoundTripsKeyMaterialAndVersion()
    {
        using var key = UserDataKey.Generate(UserId, keyVersion: 3);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        using var unwrapped = UserDataKeyProtection.Unwrap(wrapped, Password, UserId, UserKeySecretKind.Password);

        Assert.True(wrapped.AsSpan(0, 8).SequenceEqual("PLUDEK01"u8));
        Assert.Equal(UserDataKeyProtection.WrappedSize, wrapped.Length);
        Assert.Equal(3, unwrapped.KeyVersion);
        Assert.Equal(UserId, unwrapped.UserId);
        Assert.True(key.Material.SequenceEqual(unwrapped.Material));
    }

    [Fact]
    public void WrappingTwice_ProducesDifferentSaltNonceAndCiphertext()
    {
        using var key = UserDataKey.Generate(UserId);
        Assert.NotEqual(WrapForTest(key, Password, UserKeySecretKind.Password), WrapForTest(key, Password, UserKeySecretKind.Password));
    }

    [Fact]
    public void WrongPassword_IsRejected()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, "wrong password", UserId, UserKeySecretKind.Password));
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, "", UserId, UserKeySecretKind.Password));
    }

    [Fact]
    public void WrappedKeyCopiedToAnotherUser_IsRejected()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, Password, Guid.NewGuid(), UserKeySecretKind.Password));
    }

    [Fact]
    public void PasswordWrap_CannotBeOpenedAsRecoveryWrap()
    {
        using var key = UserDataKey.Generate(UserId);
        var recovery = RecoveryKey.Generate();
        var wrapped = WrapForTest(key, recovery, UserKeySecretKind.Password);
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, recovery, UserId, UserKeySecretKind.RecoveryKey));
    }

    [Fact]
    public void RecoveryWrap_AcceptsAnyDisplayFormattingOfTheSameKey()
    {
        using var key = UserDataKey.Generate(UserId);
        var recovery = RecoveryKey.Generate();
        var wrapped = WrapForTest(key, recovery, UserKeySecretKind.RecoveryKey);
        var retyped = recovery.Replace("-", " ").ToLowerInvariant();

        using var unwrapped = UserDataKeyProtection.Unwrap(wrapped, retyped, UserId, UserKeySecretKind.RecoveryKey);
        Assert.True(key.Material.SequenceEqual(unwrapped.Material));
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, "not-a-recovery-key", UserId, UserKeySecretKind.RecoveryKey));
    }

    [Fact]
    public void InvalidRecoveryKey_CannotBeUsedForWrapping()
    {
        using var key = UserDataKey.Generate(UserId);
        Assert.Throws<ArgumentException>(() => WrapForTest(key, "too-short", UserKeySecretKind.RecoveryKey));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(24)]
    [InlineData(40)]
    [InlineData(70)]
    [InlineData(-1)]
    public void TamperedHeaderSaltCiphertextOrTag_IsRejected(int offset)
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        wrapped[offset < 0 ? wrapped.Length - 1 : offset] ^= 1;
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, Password, UserId, UserKeySecretKind.Password));
    }

    [Fact]
    public void UnsupportedVersion_IsReported()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        wrapped[8] = 99;
        var exception = Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, Password, UserId, UserKeySecretKind.Password));
        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsafeKdfParameters_AreRejectedBeforeDerivation()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(12), int.MaxValue);
        var exception = Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped, Password, UserId, UserKeySecretKind.Password));
        Assert.Contains("KDF", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TruncatedWrap_IsRejected()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = WrapForTest(key, Password, UserKeySecretKind.Password);
        Assert.Throws<UserKeyException>(() => UserDataKeyProtection.Unwrap(wrapped[..^1], Password, UserId, UserKeySecretKind.Password));
    }

    [Fact]
    public void DefaultParameters_AreWrittenToTheHeader()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = UserDataKeyProtection.Wrap(key, Password, UserKeySecretKind.Password);
        Assert.Equal(64 * 1024, BinaryPrimitives.ReadInt32LittleEndian(wrapped.AsSpan(12)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(wrapped.AsSpan(16)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(wrapped.AsSpan(20)));
    }

    [Fact]
    public void DisposedKey_ClearsMaterialAndCannotBeUsed()
    {
        var key = UserDataKey.Generate(UserId);
        key.Dispose();
        Assert.Throws<ObjectDisposedException>(() => UserFieldCipher.Encrypt("x"u8, key, "Note"));
    }

    internal static byte[] WrapForTest(UserDataKey key, string secret, UserKeySecretKind kind)
        => UserDataKeyProtection.Wrap(key, secret, kind, memoryKiB: 32 * 1024, iterations: 2, parallelism: 1);
}

public sealed class EphemeralKeyWrapTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string SessionId = "3f6c1b0e9a7d4c2b8e5f0a1d2c3b4a59";

    [Fact]
    public void SessionWrap_RoundTripsWithTheSameKeyUserAndSession()
    {
        using var key = UserDataKey.Generate(UserId, keyVersion: 2);
        var sessionKey = EphemeralKeyWrap.GenerateWrappingKey();
        var wrapped = EphemeralKeyWrap.Wrap(key, sessionKey, EphemeralKeyPurpose.Session, SessionId);
        using var unwrapped = EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.Session, UserId, SessionId);

        Assert.Equal(EphemeralKeyWrap.WrappedSize, wrapped.Length);
        Assert.Equal(2, unwrapped.KeyVersion);
        Assert.True(key.Material.SequenceEqual(unwrapped.Material));
    }

    [Fact]
    public void WrongSessionKey_IsRejected()
    {
        using var key = UserDataKey.Generate(UserId);
        var wrapped = EphemeralKeyWrap.Wrap(key, EphemeralKeyWrap.GenerateWrappingKey(), EphemeralKeyPurpose.Session, SessionId);
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, EphemeralKeyWrap.GenerateWrappingKey(), EphemeralKeyPurpose.Session, UserId, SessionId));
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, new byte[16], EphemeralKeyPurpose.Session, UserId, SessionId));
    }

    [Fact]
    public void WrapIsBoundToUserSessionAndPurpose()
    {
        using var key = UserDataKey.Generate(UserId);
        var sessionKey = EphemeralKeyWrap.GenerateWrappingKey();
        var wrapped = EphemeralKeyWrap.Wrap(key, sessionKey, EphemeralKeyPurpose.Session, SessionId);

        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.Session, Guid.NewGuid(), SessionId));
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.Session, UserId, "another-session"));
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.SecondFactorPending, UserId, SessionId));
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.Session, UserId, ""));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(-1)]
    public void TamperedWrap_IsRejected(int offset)
    {
        using var key = UserDataKey.Generate(UserId);
        var sessionKey = EphemeralKeyWrap.GenerateWrappingKey();
        var wrapped = EphemeralKeyWrap.Wrap(key, sessionKey, EphemeralKeyPurpose.Session, SessionId);
        wrapped[offset < 0 ? wrapped.Length - 1 : offset] ^= 1;
        Assert.Throws<UserKeyException>(() => EphemeralKeyWrap.Unwrap(wrapped, sessionKey, EphemeralKeyPurpose.Session, UserId, SessionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("ékezet")]
    public void InvalidBindingId_IsRejectedWhenWrapping(string bindingId)
    {
        using var key = UserDataKey.Generate(UserId);
        Assert.Throws<ArgumentException>(() => EphemeralKeyWrap.Wrap(key, EphemeralKeyWrap.GenerateWrappingKey(), EphemeralKeyPurpose.Session, bindingId));
    }
}

public sealed class UserFieldCipherTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData("")]
    [InlineData("Bolt – tej, kenyér 🥖")]
    [InlineData("100% _literal_ \\ wildcards")]
    public void String_RoundTrips(string plaintext)
    {
        using var key = UserDataKey.Generate(UserId);
        var encrypted = UserFieldCipher.EncryptString(plaintext, key, "Note");
        Assert.NotEqual(plaintext, encrypted);
        Assert.Equal(plaintext, UserFieldCipher.DecryptString(encrypted, key, "Note"));
    }

    [Fact]
    public void SamePlaintext_EncryptsDifferentlyEachTime()
    {
        using var key = UserDataKey.Generate(UserId);
        Assert.NotEqual(UserFieldCipher.EncryptString("same", key, "Note"), UserFieldCipher.EncryptString("same", key, "Note"));
    }

    [Fact]
    public void AnotherUsersKey_CannotDecrypt()
    {
        using var owner = UserDataKey.Generate(UserId);
        using var other = UserDataKey.Generate(Guid.NewGuid());
        var encrypted = UserFieldCipher.EncryptString("private", owner, "Note");
        Assert.Throws<UserKeyException>(() => UserFieldCipher.DecryptString(encrypted, other, "Note"));
    }

    [Fact]
    public void SameKeyMaterialUnderAnotherUserId_CannotDecrypt()
    {
        using var owner = UserDataKey.Generate(UserId);
        using var relabelled = new UserDataKey(Guid.NewGuid(), owner.KeyVersion, owner.Material.ToArray());
        var encrypted = UserFieldCipher.EncryptString("private", owner, "Note");
        Assert.Throws<UserKeyException>(() => UserFieldCipher.DecryptString(encrypted, relabelled, "Note"));
    }

    [Fact]
    public void ValueMovedToAnotherFieldGroup_CannotDecrypt()
    {
        using var key = UserDataKey.Generate(UserId);
        var encrypted = UserFieldCipher.EncryptString("private", key, "Note");
        Assert.Throws<UserKeyException>(() => UserFieldCipher.DecryptString(encrypted, key, "Name"));
    }

    [Fact]
    public void KeyVersionMismatch_IsReported()
    {
        using var key = UserDataKey.Generate(UserId, keyVersion: 1);
        using var rotated = new UserDataKey(UserId, 2, key.Material.ToArray());
        var encrypted = UserFieldCipher.EncryptString("private", key, "Note");
        var exception = Assert.Throws<UserKeyException>(() => UserFieldCipher.DecryptString(encrypted, rotated, "Note"));
        Assert.Contains("version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(-1)]
    public void TamperedCiphertext_IsRejected(int offset)
    {
        using var key = UserDataKey.Generate(UserId);
        var encrypted = UserFieldCipher.Encrypt(Encoding.UTF8.GetBytes("private note"), key, "Note");
        encrypted[offset < 0 ? encrypted.Length - 1 : offset] ^= 1;
        Assert.Throws<UserKeyException>(() => UserFieldCipher.Decrypt(encrypted, key, "Note"));
    }

    [Theory]
    [InlineData("not base64!")]
    [InlineData("AAAA")]
    public void MalformedStoredValue_IsRejected(string stored)
    {
        using var key = UserDataKey.Generate(UserId);
        Assert.Throws<UserKeyException>(() => UserFieldCipher.DecryptString(stored, key, "Note"));
    }

    [Fact]
    public void BinaryPayload_RoundTripsWithFixedOverhead()
    {
        using var key = UserDataKey.Generate(UserId);
        var payload = RandomNumberGenerator.GetBytes(4096);
        var encrypted = UserFieldCipher.Encrypt(payload, key, "Cache.GetSummaryAsync");
        Assert.Equal(payload.Length + UserFieldCipher.Overhead, encrypted.Length);
        Assert.Equal(payload, UserFieldCipher.Decrypt(encrypted, key, "Cache.GetSummaryAsync"));
    }
}

public sealed class RecoveryKeyTests
{
    [Fact]
    public void Generate_ProducesSixGroupsOfFiveCrockfordSymbols()
    {
        var key = RecoveryKey.Generate();
        var groups = key.Split('-');
        Assert.Equal(6, groups.Length);
        Assert.All(groups, group => Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}$", group));
    }

    [Fact]
    public void Generate_IsRandom()
    {
        var keys = Enumerable.Range(0, 100).Select(_ => RecoveryKey.Generate()).ToHashSet();
        Assert.Equal(100, keys.Count);
    }

    [Theory]
    [InlineData("abcde-fghjk-mnpqr-stvwx-yz012-34567", "ABCDEFGHJKMNPQRSTVWXYZ01234567")]
    [InlineData(" ABCDE FGHJK\tMNPQR-STVWX YZ012 34567 ", "ABCDEFGHJKMNPQRSTVWXYZ01234567")]
    [InlineData("oOiIl-LAAAA-AAAAA-AAAAA-AAAAA-AAAAA", "00111" + "1AAAAAAAAAAAAAAAAAAAAAAAA")]
    public void TryNormalize_AcceptsDisplayVariants(string input, string expected)
    {
        Assert.True(RecoveryKey.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012-345678")]
    [InlineData("UBCDE-FGHJK-MNPQR-STVWX-YZ012-34567")]
    [InlineData("ABCDE-FGHJK-MNPQR-STVWX-YZ012-3456!")]
    public void TryNormalize_RejectsInvalidKeys(string input)
    {
        Assert.False(RecoveryKey.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
    }
}

public sealed class UserKeyContextTests
{
    [Fact]
    public void Required_ThrowsWithoutAnUnlockedKey()
    {
        Assert.Null(UserKeyContext.Current);
        Assert.Throws<UserKeyUnavailableException>(() => UserKeyContext.Required);
    }

    [Fact]
    public async Task Scope_FlowsIntoAwaitedWorkAndIsClearedOnDispose()
    {
        var key = UserDataKey.Generate(Guid.NewGuid());
        using (UserKeyContext.Enter(key))
        {
            await Task.Yield();
            Assert.Same(key, UserKeyContext.Current);
            Assert.Same(key, await Task.Run(() => UserKeyContext.Current));
        }
        Assert.Null(UserKeyContext.Current);
        Assert.Throws<ObjectDisposedException>(() => key.Material.Length);
    }

    [Fact]
    public async Task WorkThatOutlivesTheScope_LosesTheKey()
    {
        var release = new TaskCompletionSource();
        Task<UserDataKey?> straggler;
        using (UserKeyContext.Enter(UserDataKey.Generate(Guid.NewGuid())))
        {
            straggler = Task.Run(async () =>
            {
                await release.Task;
                return UserKeyContext.Current;
            });
        }
        release.SetResult();
        Assert.Null(await straggler);
    }

    [Fact]
    public void NestedScope_RestoresTheOuterKey()
    {
        var outer = UserDataKey.Generate(Guid.NewGuid());
        using (UserKeyContext.Enter(outer))
        {
            using (UserKeyContext.Enter(UserDataKey.Generate(Guid.NewGuid())))
                Assert.NotSame(outer, UserKeyContext.Current);
            Assert.Same(outer, UserKeyContext.Current);
        }
        Assert.Null(UserKeyContext.Current);
    }

    [Fact]
    public void DisposingTwice_IsHarmless()
    {
        var scope = UserKeyContext.Enter(UserDataKey.Generate(Guid.NewGuid()));
        scope.Dispose();
        scope.Dispose();
        Assert.Null(UserKeyContext.Current);
    }
}
