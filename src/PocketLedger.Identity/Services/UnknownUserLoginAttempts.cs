using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace PocketLedger.Services;

public readonly record struct LoginAttemptStatus(int RemainingAttempts, DateTimeOffset? LockedUntilUtc);

public interface IUnknownUserLoginAttempts
{
    LoginAttemptStatus RecordFailure(string normalizedUsername);
}

// Unknown usernames get the same countdown and lockout as real accounts, so the sign-in answer does not reveal which usernames exist.
public sealed class UnknownUserLoginAttempts(IOptions<IdentityOptions> options, TimeProvider timeProvider) : IUnknownUserLoginAttempts, IDisposable
{
    private static readonly TimeSpan RetainedFor = TimeSpan.FromHours(24);
    private readonly MemoryCache attempts = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly Lock gate = new();

    public LoginAttemptStatus RecordFailure(string normalizedUsername)
    {
        var lockout = options.Value.Lockout;
        var now = timeProvider.GetUtcNow();
        lock (gate)
        {
            var entry = attempts.GetOrCreate(normalizedUsername, created =>
            {
                created.Size = 1;
                created.SlidingExpiration = RetainedFor;
                return new Entry();
            })!;
            if (entry.LockedUntilUtc > now) return new LoginAttemptStatus(0, entry.LockedUntilUtc);
            entry.LockedUntilUtc = null;
            if (++entry.FailedCount < lockout.MaxFailedAccessAttempts) return new LoginAttemptStatus(lockout.MaxFailedAccessAttempts - entry.FailedCount, null);
            entry.FailedCount = 0;
            entry.LockedUntilUtc = now + lockout.DefaultLockoutTimeSpan;
            return new LoginAttemptStatus(0, entry.LockedUntilUtc);
        }
    }

    public void Dispose() => attempts.Dispose();

    private sealed class Entry
    {
        public int FailedCount { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
    }
}
