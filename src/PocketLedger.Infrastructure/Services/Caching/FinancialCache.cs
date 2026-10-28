using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PocketLedger.Data;
using PocketLedger.Security;
using StackExchange.Redis;

namespace PocketLedger.Services;

public sealed class FinancialCacheOptions
{
    public string? ConnectionString { get; set; }
    public int LifetimeMinutes { get; set; } = 30;
}

public sealed class FinancialCache(PocketLedgerDbContext dbContext, ICurrentUser currentUser, IUserContextService userContext, IDistributedCache store, IOptions<FinancialCacheOptions> options, ILogger<FinancialCache> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };

    public async Task<bool> IncludesMonthAsync(int year, int month, int months, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString) || year is < 1 or > 9999 || month is < 1 or > 12) return false;
        var today = await userContext.TodayAsync(cancellationToken);
        var distance = (today.Year - year) * 12 + today.Month - month;
        return distance >= 0 && distance < months;
    }

    public async Task<bool> IncludesFilterAsync(TransactionFilter filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString)) return false;
        DateOnly? from = filter.DateFrom;
        DateOnly? to = filter.DateTo;
        if (filter.Year is >= 1 and <= 9999 && filter.Month is >= 1 and <= 12)
        {
            var first = new DateOnly(filter.Year.Value, filter.Month.Value, 1);
            var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month));
            from = from is null || from < first ? first : from;
            to = to is null || to > last ? last : to;
        }
        return from is not null && to is not null && from <= to
            && await IncludesMonthAsync(from.Value.Year, from.Value.Month, 2, cancellationToken)
            && await IncludesMonthAsync(to.Value.Year, to.Value.Month, 2, cancellationToken);
    }

    public async Task<T> GetOrCreateAsync<T>(string operation, object dimensions, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString) || !currentUser.IsAuthenticated || dbContext.Database.CurrentTransaction is not null) return await read();
        // Cached results contain the user's names and notes, so they are only cached under the user's own data key.
        var dataKey = UserKeyContext.Current;
        if (dbContext.Encryption is not null && dataKey is null) return await read();
        var cacheGroup = "Cache." + operation;

        // The revision commits atomically with financial writes, including imports and the recurring worker.
        // A fill racing a commit stays under the old revision and cannot poison subsequent reads.
        var revision = await dbContext.Database.SqlQuery<Guid>($"SELECT revision AS \"Value\" FROM financial_cache_revisions WHERE owner_id = {currentUser.UserId}").SingleOrDefaultAsync(cancellationToken);
        var dimensionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dimensions))));
        var key = $"pocketledger:finance:v3:{currentUser.UserId:N}:{revision:N}:{operation}:{dimensionHash}";
        var protector = dbContext.Encryption?.CreateProtector($"FinancialCache.{currentUser.UserId:N}.{operation}");
        try
        {
            var bytes = await store.GetAsync(key, cancellationToken);
            if (bytes is not null && JsonSerializer.Deserialize<T>(protector is null ? bytes : Open(protector, dataKey!, cacheGroup, bytes), JsonOptions) is { } cached) return cached;
        }
        catch (Exception exception) when (exception is RedisException or JsonException or CryptographicException)
        {
            logger.LogWarning(exception, "Financial cache read failed for {Operation}; reading from the database.", operation);
            return await read();
        }

        var result = await read();
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
            await store.SetAsync(key, protector is null ? bytes : Seal(protector, dataKey!, cacheGroup, bytes), new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(options.Value.LifetimeMinutes) }, cancellationToken);
        }
        catch (Exception exception) when (exception is RedisException or JsonException or CryptographicException)
        {
            logger.LogWarning(exception, "Financial cache write failed for {Operation}; returning the database result.", operation);
        }
        return result;
    }

    // The user's key inside, the host's Data Protection layer outside, as for the database fields.
    internal static byte[] Seal(IDataProtector protector, UserDataKey key, string group, byte[] payload) => protector.Protect(UserFieldCipher.Encrypt(payload, key, group));

    internal static byte[] Open(IDataProtector protector, UserDataKey key, string group, byte[] stored) => UserFieldCipher.Decrypt(protector.Unprotect(stored), key, group);
}
