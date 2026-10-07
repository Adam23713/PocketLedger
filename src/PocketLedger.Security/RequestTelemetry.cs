using System.Security.Claims;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PocketLedger.Security;

public sealed class RequestTelemetryOptions
{
    public const string SectionName = "RequestTelemetry";
    public string? ConnectionString { get; set; }
    public int RetentionHours { get; set; } = 48;
    public int BucketMinutes { get; set; } = 5;
}

public sealed record RequestTelemetryBucketSnapshot(DateTimeOffset StartedAtUtc, long TotalRequests, long AuthenticatedRequests, long AnonymousRequests, long? SelectedUserRequests);

public sealed record RequestTelemetryStatistics(bool IsAvailable, DateTimeOffset FromUtc, DateTimeOffset ToUtc, Guid? UserId, long TotalRequests, long AuthenticatedRequests, long AnonymousRequests, long? SelectedUserRequests, double AverageRequestsPerMinute, IReadOnlyList<RequestTelemetryBucketSnapshot> Buckets);

public interface IRequestTelemetryReader
{
    Task<RequestTelemetryStatistics> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? userId = null, CancellationToken cancellationToken = default);
}

public static class RequestTelemetryBuckets
{
    public static DateTimeOffset Start(DateTimeOffset timestamp, int bucketMinutes = 5)
    {
        var utc = timestamp.ToUniversalTime();
        var ticks = TimeSpan.FromMinutes(bucketMinutes).Ticks;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % ticks, TimeSpan.Zero);
    }
}

public static class RelevantRequestClassifier
{
    private static readonly HashSet<string> StaticExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".avif", ".css", ".gif", ".ico", ".jpeg", ".jpg", ".js", ".map", ".png", ".svg", ".webp", ".woff", ".woff2"
    };

    public static bool IsRelevant(PathString path)
    {
        if (!path.HasValue) return false;
        var value = path.Value!;
        if (value.Equals("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/.well-known/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/openapi/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) return false;
        return !StaticExtensions.Contains(Path.GetExtension(value));
    }
}

public static class RequestTelemetryRegistration
{
    public static IServiceCollection AddRequestTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RequestTelemetryOptions>().Bind(configuration.GetSection(RequestTelemetryOptions.SectionName))
            .Validate(options => options.RetentionHours is >= 1 and <= 48, "Request telemetry retention must be between 1 and 48 hours.")
            .Validate(options => options.BucketMinutes == 5, "Request telemetry uses five-minute buckets.")
            .ValidateOnStart();
        services.AddSingleton<RequestTelemetryService>();
        services.AddSingleton<IRequestTelemetryReader>(services => services.GetRequiredService<RequestTelemetryService>());
        services.AddSingleton<IHostedService>(services => services.GetRequiredService<RequestTelemetryService>());
        services.AddSingleton<SuspiciousRequestDetectionService>();
        services.AddSingleton<ISuspiciousRequestEventReader>(services => services.GetRequiredService<SuspiciousRequestDetectionService>());
        return services;
    }

    public static IApplicationBuilder UseRequestTelemetry(this IApplicationBuilder app) => app.UseMiddleware<RequestTelemetryMiddleware>();
}

internal readonly record struct RequestTelemetryObservation(DateTimeOffset BucketStartUtc, Guid? UserId);

internal sealed class RequestTelemetryMiddleware(RequestDelegate next, RequestTelemetryService telemetry, SuspiciousRequestDetectionService suspiciousRequests, TimeProvider timeProvider)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (RelevantRequestClassifier.IsRelevant(context.Request.Path))
        {
            var timestamp = timeProvider.GetUtcNow();
            telemetry.TryRecord(context.User, timestamp);
            suspiciousRequests.TryRecord(context, timestamp);
        }
        await next(context);
    }
}

internal sealed class RequestTelemetryService(IOptions<RequestTelemetryOptions> options, SuspiciousRequestDetectionService suspiciousRequests, ILogger<RequestTelemetryService> logger) : BackgroundService, IRequestTelemetryReader
{
    private const string KeyPrefix = "pocketledger:request-telemetry:v1:";
    private const string TotalField = "total";
    private const string AnonymousField = "anonymous";
    private const string IncrementScript = "redis.call('HINCRBY', KEYS[1], 'total', ARGV[1]); redis.call('HINCRBY', KEYS[1], ARGV[2], ARGV[1]); return redis.call('PEXPIREAT', KEYS[1], ARGV[3])";
    private readonly Channel<RequestTelemetryObservation> queue = Channel.CreateBounded<RequestTelemetryObservation>(new BoundedChannelOptions(8192) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private IConnectionMultiplexer? connection;
    private long lastFailureLogUtcTicks;

    public void TryRecord(ClaimsPrincipal principal, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString)) return;
        Guid? userId = null;
        if (principal.Identity?.IsAuthenticated == true)
        {
            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
            if (Guid.TryParse(value, out var parsed)) userId = parsed;
        }
        queue.Writer.TryWrite(new RequestTelemetryObservation(RequestTelemetryBuckets.Start(timestamp, options.Value.BucketMinutes), userId));
    }

    public async Task<RequestTelemetryStatistics> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();
        if (toUtc <= fromUtc) throw new ArgumentException("The telemetry interval end must be after its start.", nameof(toUtc));
        if (toUtc - fromUtc > TimeSpan.FromHours(options.Value.RetentionHours)) throw new ArgumentOutOfRangeException(nameof(fromUtc), "The telemetry interval cannot exceed the configured retention period.");

        var bucketDuration = TimeSpan.FromMinutes(options.Value.BucketMinutes);
        fromUtc = RequestTelemetryBuckets.Start(fromUtc, options.Value.BucketMinutes);
        toUtc = RequestTelemetryBuckets.Start(toUtc.AddTicks(bucketDuration.Ticks - 1), options.Value.BucketMinutes);
        var starts = new List<DateTimeOffset>();
        var bucketStart = fromUtc;
        while (bucketStart < toUtc)
        {
            starts.Add(bucketStart);
            bucketStart = bucketStart.AddMinutes(options.Value.BucketMinutes);
        }

        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString)) return EmptyStatistics(fromUtc, toUtc, userId, starts, false);
        try
        {
            var database = (await GetConnectionAsync(cancellationToken)).GetDatabase();
            RedisValue[] fields = userId is null ? [TotalField, AnonymousField] : [TotalField, AnonymousField, UserField(userId.Value)];
            var reads = starts.Select(start => database.HashGetAsync(Key(start), fields)).ToArray();
            await Task.WhenAll(reads).WaitAsync(cancellationToken);
            var buckets = starts.Select((start, index) => ToSnapshot(start, reads[index].Result, userId is not null)).ToArray();
            return ToStatistics(fromUtc, toUtc, userId, buckets, true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Request telemetry query failed; returning an empty result.");
            return EmptyStatistics(fromUtc, toUtc, userId, starts, false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var suspiciousRequestWorker = suspiciousRequests.RunAsync(stoppingToken);
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                var observations = new List<RequestTelemetryObservation>(8192);
                while (observations.Count < 8192 && queue.Reader.TryRead(out var observation)) observations.Add(observation);
                if (observations.Count == 0) continue;
                try
                {
                    var database = (await GetConnectionAsync(stoppingToken)).GetDatabase();
                    var writes = observations.GroupBy(item => new { item.BucketStartUtc, item.UserId }).Select(group =>
                    {
                        var count = group.LongCount();
                        var key = Key(group.Key.BucketStartUtc);
                        RedisValue field = group.Key.UserId is { } userId ? UserField(userId) : AnonymousField;
                        var expiresAtUnixMilliseconds = group.Key.BucketStartUtc.AddHours(options.Value.RetentionHours).ToUnixTimeMilliseconds();
                        return database.ScriptEvaluateAsync(IncrementScript, [key], [count, field, expiresAtUnixMilliseconds]);
                    });
                    await Task.WhenAll(writes);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    LogFailure(exception, "Request telemetry write failed; observations were discarded.");
                }
            }
        }
        finally
        {
            await suspiciousRequestWorker;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Writer.TryComplete();
        suspiciousRequests.Complete();
        await base.StopAsync(cancellationToken);
        if (connection is not null) await connection.DisposeAsync();
        await suspiciousRequests.DisposeAsync();
    }

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection is not null) return connection;
        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (connection is not null) return connection;
            var configuration = ConfigurationOptions.Parse(options.Value.ConnectionString!);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 500;
            configuration.AsyncTimeout = 500;
            configuration.ConnectRetry = 0;
            configuration.BacklogPolicy = BacklogPolicy.FailFast;
            connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            return connection;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private static RequestTelemetryBucketSnapshot ToSnapshot(DateTimeOffset start, RedisValue[] values, bool hasSelectedUser)
    {
        var total = Parse(values[0]);
        var anonymous = Parse(values[1]);
        return new RequestTelemetryBucketSnapshot(start, total, Math.Max(0, total - anonymous), anonymous, hasSelectedUser ? Parse(values[2]) : null);
    }

    private static RequestTelemetryStatistics ToStatistics(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? userId, IReadOnlyList<RequestTelemetryBucketSnapshot> buckets, bool isAvailable)
    {
        var total = buckets.Sum(bucket => bucket.TotalRequests);
        var authenticated = buckets.Sum(bucket => bucket.AuthenticatedRequests);
        var anonymous = buckets.Sum(bucket => bucket.AnonymousRequests);
        long? selected = userId is null ? null : buckets.Sum(bucket => bucket.SelectedUserRequests ?? 0);
        var minutes = (toUtc - fromUtc).TotalMinutes;
        var averageSource = selected ?? total;
        return new RequestTelemetryStatistics(isAvailable, fromUtc, toUtc, userId, total, authenticated, anonymous, selected, averageSource / minutes, buckets);
    }

    private static RequestTelemetryStatistics EmptyStatistics(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? userId, IEnumerable<DateTimeOffset> starts, bool isAvailable)
        => ToStatistics(fromUtc, toUtc, userId, starts.Select(start => new RequestTelemetryBucketSnapshot(start, 0, 0, 0, userId is null ? null : 0)).ToArray(), isAvailable);

    private static long Parse(RedisValue value) => value.HasValue && long.TryParse(value.ToString(), out var parsed) ? parsed : 0;
    private static RedisKey Key(DateTimeOffset bucketStart) => $"{KeyPrefix}{bucketStart.ToUnixTimeSeconds()}";
    private static RedisValue UserField(Guid userId) => $"user:{userId:N}";

    private void LogFailure(Exception exception, string message)
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref lastFailureLogUtcTicks);
        if (now - previous < TimeSpan.FromMinutes(1).Ticks || Interlocked.CompareExchange(ref lastFailureLogUtcTicks, now, previous) != previous) return;
        logger.LogWarning(exception, message);
    }
}
