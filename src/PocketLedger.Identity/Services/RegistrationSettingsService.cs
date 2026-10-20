using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public sealed record RegistrationStatus(bool IsOpen, int? MaximumUserCount, int RegisteredUserCount)
{
    public int? RemainingSlots => MaximumUserCount is { } maximum ? Math.Max(0, maximum - RegisteredUserCount) : null;
    public bool IsFull => RemainingSlots == 0;
    public bool AcceptsRegistrations => IsOpen && !IsFull;
}

public interface IRegistrationSettingsService
{
    Task<RegistrationStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(bool isOpen, int? maximumUserCount, CancellationToken cancellationToken = default);
    // Serializes the capacity check with the user creation so concurrent sign-ups cannot exceed the limit.
    Task<IDisposable> AcquireRegistrationLockAsync(CancellationToken cancellationToken = default);
}

public sealed class RegistrationSettingsService(IdentityDbContext dbContext, IOptions<PublicRegistrationOptions> defaults, TimeProvider timeProvider) : IRegistrationSettingsService
{
    private static readonly SemaphoreSlim RegistrationLock = new(1, 1);

    public async Task<RegistrationStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await dbContext.RegistrationSettings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == RegistrationSettings.SingletonId, cancellationToken);
        var registeredUserCount = await dbContext.Users.CountAsync(cancellationToken);
        return settings is null
            ? new RegistrationStatus(defaults.Value.Enabled, defaults.Value.MaximumUserCount, registeredUserCount)
            : new RegistrationStatus(settings.IsOpen, settings.MaximumUserCount, registeredUserCount);
    }

    public async Task UpdateAsync(bool isOpen, int? maximumUserCount, CancellationToken cancellationToken = default)
    {
        if (maximumUserCount is < 1 or > PublicRegistrationOptions.MaximumAllowedUserCount)
            throw new ArgumentOutOfRangeException(nameof(maximumUserCount));
        var settings = await dbContext.RegistrationSettings.SingleOrDefaultAsync(item => item.Id == RegistrationSettings.SingletonId, cancellationToken);
        if (settings is null) dbContext.RegistrationSettings.Add(settings = new RegistrationSettings());
        settings.IsOpen = isOpen;
        settings.MaximumUserCount = maximumUserCount;
        settings.UpdatedAtUtc = timeProvider.GetUtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IDisposable> AcquireRegistrationLockAsync(CancellationToken cancellationToken = default)
    {
        await RegistrationLock.WaitAsync(cancellationToken);
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0) RegistrationLock.Release();
        }
    }
}
