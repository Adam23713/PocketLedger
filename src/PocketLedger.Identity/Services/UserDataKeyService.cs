using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Services;

public sealed record CreatedUserDataKey(UserDataKey Key, string RecoveryKey) : IDisposable
{
    public void Dispose() => Key.Dispose();
}

public interface IUserDataKeyService
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CreatedUserDataKey> CreateAsync(Guid userId, string password, CancellationToken cancellationToken = default);
    // Returns null when the user has no data key; throws UserKeyException when the secret does not open it.
    Task<UserDataKey?> UnlockAsync(Guid userId, string secret, UserKeySecretKind kind, CancellationToken cancellationToken = default);
    Task ReplacePasswordAsync(UserDataKey key, string newPassword, CancellationToken cancellationToken = default);
    Task<string> ReplaceRecoveryKeyAsync(UserDataKey key, CancellationToken cancellationToken = default);
}

public sealed class UserDataKeyService(IdentityDbContext dbContext, UserKeyDerivationLimiter limiter, TimeProvider timeProvider) : IUserDataKeyService
{
    public const int CurrentKeyVersion = 1;

    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default)
        => dbContext.UserDataKeys.AnyAsync(item => item.UserId == userId, cancellationToken);

    public async Task<CreatedUserDataKey> CreateAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        if (await ExistsAsync(userId, cancellationToken)) throw new InvalidOperationException("The user already has a data key.");
        var key = UserDataKey.Generate(userId, CurrentKeyVersion);
        try
        {
            var recoveryKey = RecoveryKey.Generate();
            var passwordWrap = await WrapAsync(key, password, UserKeySecretKind.Password, cancellationToken);
            var recoveryWrap = await WrapAsync(key, recoveryKey, UserKeySecretKind.RecoveryKey, cancellationToken);
            var now = timeProvider.GetUtcNow();
            dbContext.UserDataKeys.Add(new UserDataKeyRecord { UserId = userId, KeyVersion = key.KeyVersion, PasswordWrap = passwordWrap, RecoveryWrap = recoveryWrap, CreatedAtUtc = now, UpdatedAtUtc = now });
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CreatedUserDataKey(key, recoveryKey);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    public async Task<UserDataKey?> UnlockAsync(Guid userId, string secret, UserKeySecretKind kind, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.UserDataKeys.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (record is null) return null;
        var wrapped = Decode(kind == UserKeySecretKind.Password ? record.PasswordWrap : record.RecoveryWrap);
        var key = await limiter.RunAsync(() => UserDataKeyProtection.Unwrap(wrapped, secret, userId, kind), cancellationToken);
        if (key.KeyVersion != record.KeyVersion)
        {
            key.Dispose();
            throw new UserKeyException("The stored data key version does not match its wrapped key.");
        }
        return key;
    }

    public async Task ReplacePasswordAsync(UserDataKey key, string newPassword, CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(key, cancellationToken);
        record.PasswordWrap = await WrapAsync(key, newPassword, UserKeySecretKind.Password, cancellationToken);
        record.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> ReplaceRecoveryKeyAsync(UserDataKey key, CancellationToken cancellationToken = default)
    {
        var record = await GetRecordAsync(key, cancellationToken);
        var recoveryKey = RecoveryKey.Generate();
        record.RecoveryWrap = await WrapAsync(key, recoveryKey, UserKeySecretKind.RecoveryKey, cancellationToken);
        record.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return recoveryKey;
    }

    private async Task<UserDataKeyRecord> GetRecordAsync(UserDataKey key, CancellationToken cancellationToken)
    {
        var record = await dbContext.UserDataKeys.SingleOrDefaultAsync(item => item.UserId == key.UserId, cancellationToken) ?? throw new InvalidOperationException("The user has no data key.");
        if (record.KeyVersion != key.KeyVersion) throw new InvalidOperationException("The unlocked data key does not match the stored key version.");
        return record;
    }

    private async Task<string> WrapAsync(UserDataKey key, string secret, UserKeySecretKind kind, CancellationToken cancellationToken)
        => Convert.ToBase64String(await limiter.RunAsync(() => UserDataKeyProtection.Wrap(key, secret, kind), cancellationToken));

    private static byte[] Decode(string wrapped)
    {
        try
        {
            return Convert.FromBase64String(wrapped);
        }
        catch (FormatException exception)
        {
            throw new UserKeyException("The stored data key is damaged.", exception);
        }
    }
}

// Each Argon2id derivation holds 64 MiB, so concurrent derivations are capped to keep login bursts from exhausting memory.
public sealed class UserKeyDerivationLimiter(IOptions<AuthenticationSecurityOptions> options) : IDisposable
{
    private readonly TimeSpan maximumWait = TimeSpan.FromSeconds(options.Value.DataKeyDerivationWaitSeconds);
    private readonly SemaphoreSlim slots = new(options.Value.DataKeyDerivationConcurrency, options.Value.DataKeyDerivationConcurrency);

    public async Task<T> RunAsync<T>(Func<T> derive, CancellationToken cancellationToken)
    {
        if (!await slots.WaitAsync(maximumWait, cancellationToken)) throw new UserKeyDerivationBusyException();
        try
        {
            return derive();
        }
        finally
        {
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}

public sealed class UserKeyDerivationBusyException() : InvalidOperationException("Too many data key operations are in progress. Try again shortly.");
