using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PocketLedger.Security;

public sealed record SuspiciousRequestEvent(DateTimeOffset DetectedAtUtc, DateTimeOffset LastObservedAtUtc, string ClientIpAddress, long RequestCount, TimeSpan Window,
    IReadOnlyList<Guid> UserIds, IReadOnlyList<string> Applications, IReadOnlyList<string> Hosts);

public sealed record SuspiciousRequestEventsResult(bool IsAvailable, IReadOnlyList<SuspiciousRequestEvent> Events);

public interface ISuspiciousRequestEventReader
{
    Task<SuspiciousRequestEventsResult> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default);
}

internal readonly record struct SuspiciousRequestObservation(DateTimeOffset TimestampUtc, string ClientIpAddress, Guid? UserId, string Application, string Host);

internal sealed class SuspiciousRequestDetectionService(IOptions<RequestTelemetryOptions> telemetryOptions, RequestTelemetryConnection store, ILogger<SuspiciousRequestDetectionService> logger)
    : ISuspiciousRequestEventReader
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan IncidentRetention = TimeSpan.FromHours(48);
    private const int Threshold = 200;
    private const string KeyPrefix = "pocketledger:suspicious-requests:v1:";
    private const int BatchSize = 1024;
    // Adds a batch of observations for one address. The rolling set keeps at most Threshold + 1 entries, which is enough to decide
    // whether the threshold is exceeded; once an incident is active its request count grows by the batch size instead of the set size,
    // so memory per address stays bounded during a flood. The window entries are returned when an incident is created so its context can be filled.
    private const string DetectionScript = """
        local count = tonumber(ARGV[10])
        for index = 0, count - 1 do redis.call('ZADD', KEYS[1], ARGV[11 + index * 2], ARGV[12 + index * 2]) end
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
        redis.call('PEXPIRE', KEYS[1], ARGV[3])
        local threshold = tonumber(ARGV[4])
        local windowCount = redis.call('ZCARD', KEYS[1])
        if windowCount <= threshold then return {'', windowCount, 0, {}} end
        local id = redis.call('GET', KEYS[2])
        local created = 0
        local members = {}
        if not id then
            id = ARGV[5]
            created = 1
            redis.call('SET', KEYS[2], id, 'PX', ARGV[3])
            redis.call('ZADD', KEYS[3], ARGV[1], id)
            members = redis.call('ZRANGE', KEYS[1], 0, -1)
        else
            redis.call('PEXPIRE', KEYS[2], ARGV[3])
        end
        local incident = ARGV[6] .. id
        if created == 1 then
            redis.call('HSET', incident, 'detectedAt', ARGV[1], 'address', ARGV[7], 'windowSeconds', ARGV[8], 'requestCount', windowCount)
        else
            redis.call('HINCRBY', incident, 'requestCount', count)
        end
        redis.call('HSET', incident, 'lastObservedAt', ARGV[1])
        redis.call('PEXPIRE', incident, ARGV[9])
        redis.call('PEXPIRE', KEYS[3], ARGV[9])
        redis.call('ZREMRANGEBYRANK', KEYS[1], 0, -(threshold + 2))
        return {id, windowCount, created, members}
        """;
    private readonly Channel<SuspiciousRequestObservation> queue = Channel.CreateBounded<SuspiciousRequestObservation>(new BoundedChannelOptions(8192) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = false });
    private long lastFailureLogUtcTicks;

    public void TryRecord(HttpContext context, DateTimeOffset timestamp)
    {
        if (string.IsNullOrWhiteSpace(telemetryOptions.Value.ConnectionString) || context.Connection.RemoteIpAddress is not { } address) return;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        Guid? userId = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub");
            if (Guid.TryParse(value, out var parsed)) userId = parsed;
        }
        var application = context.RequestServices.GetService<IHostEnvironment>()?.ApplicationName ?? "Unknown";
        queue.Writer.TryWrite(new SuspiciousRequestObservation(timestamp.ToUniversalTime(), address.ToString(), userId, application, context.Request.Host.Value ?? string.Empty));
    }

    public async Task<SuspiciousRequestEventsResult> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (string.IsNullOrWhiteSpace(telemetryOptions.Value.ConnectionString)) return new SuspiciousRequestEventsResult(false, []);
        try
        {
            var database = await store.GetDatabaseAsync(cancellationToken);
            var ids = await database.SortedSetRangeByRankAsync(IncidentsKey(), -limit, -1, Order.Descending).WaitAsync(cancellationToken);
            var reads = ids.Select(id => ReadEventAsync(database, id.ToString(), cancellationToken)).ToArray();
            var events = (await Task.WhenAll(reads)).Where(item => item is not null).Cast<SuspiciousRequestEvent>().ToArray();
            return new SuspiciousRequestEventsResult(true, events);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Suspicious request events could not be read.");
            return new SuspiciousRequestEventsResult(false, []);
        }
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        var batch = new List<SuspiciousRequestObservation>(BatchSize);
        while (await queue.Reader.WaitToReadAsync(stoppingToken))
        {
            batch.Clear();
            while (batch.Count < BatchSize && queue.Reader.TryRead(out var observation)) batch.Add(observation);
            try
            {
                var database = await store.GetDatabaseAsync(stoppingToken);
                await Task.WhenAll(batch.GroupBy(item => item.ClientIpAddress).Select(group => RecordAsync(database, group.Key, group.ToArray(), stoppingToken)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogFailure(exception, "Suspicious request detection failed; request processing was not affected.");
            }
        }
    }

    public void Complete() => queue.Writer.TryComplete();

    private static async Task RecordAsync(IDatabase database, string clientIpAddress, SuspiciousRequestObservation[] observations, CancellationToken cancellationToken)
    {
        var addressKey = AddressKey(clientIpAddress);
        var now = observations.Max(item => item.TimestampUtc).ToUnixTimeMilliseconds();
        var arguments = new List<RedisValue>(10 + observations.Length * 2)
        {
            now, now - (long)Window.TotalMilliseconds, (long)Window.TotalMilliseconds, Threshold, Guid.NewGuid().ToString("N"), IncidentKeyPrefix(),
            clientIpAddress, (long)Window.TotalSeconds, (long)IncidentRetention.TotalMilliseconds, observations.Length
        };
        foreach (var observation in observations)
        {
            arguments.Add(observation.TimestampUtc.ToUnixTimeMilliseconds());
            arguments.Add(ObservationMember(observation));
        }
        var result = await database.ScriptEvaluateAsync(DetectionScript, [RollingKey(addressKey), ActiveIncidentKey(addressKey), IncidentsKey()], [.. arguments]).WaitAsync(cancellationToken);
        var values = (RedisResult[]?)result ?? throw new InvalidOperationException("The suspicious request detection script returned an invalid result.");
        var activeIncidentId = values[0].ToString();
        if (string.IsNullOrEmpty(activeIncidentId)) return;
        var context = (long)values[2] == 1
            ? ((RedisResult[]?)values[3] ?? []).Select(value => ParseObservation(value.ToString())).Where(value => value is not null).Select(value => value!.Value)
            : observations;
        await UpdateIncidentContextAsync(database, activeIncidentId, context, cancellationToken);
    }

    private static async Task UpdateIncidentContextAsync(IDatabase database, string incidentId, IEnumerable<SuspiciousRequestObservation> observations, CancellationToken cancellationToken)
    {
        var values = observations.ToArray();
        var expiry = IncidentRetention;
        var tasks = new List<Task>();
        var users = values.Where(value => value.UserId is not null).Select(value => (RedisValue)value.UserId!.Value.ToString("D")).Distinct().ToArray();
        var applications = values.Select(value => (RedisValue)value.Application).Where(value => value.HasValue).Distinct().ToArray();
        var hosts = values.Select(value => (RedisValue)value.Host).Where(value => value.HasValue).Distinct().ToArray();
        if (users.Length > 0) tasks.Add(database.SetAddAsync(IncidentUsersKey(incidentId), users));
        if (applications.Length > 0) tasks.Add(database.SetAddAsync(IncidentApplicationsKey(incidentId), applications));
        if (hosts.Length > 0) tasks.Add(database.SetAddAsync(IncidentHostsKey(incidentId), hosts));
        tasks.Add(database.KeyExpireAsync(IncidentUsersKey(incidentId), expiry));
        tasks.Add(database.KeyExpireAsync(IncidentApplicationsKey(incidentId), expiry));
        tasks.Add(database.KeyExpireAsync(IncidentHostsKey(incidentId), expiry));
        await Task.WhenAll(tasks).WaitAsync(cancellationToken);
    }

    private static async Task<SuspiciousRequestEvent?> ReadEventAsync(IDatabase database, string incidentId, CancellationToken cancellationToken)
    {
        var fields = await database.HashGetAllAsync(IncidentKey(incidentId)).WaitAsync(cancellationToken);
        if (fields.Length == 0) return null;
        var values = fields.ToDictionary(item => item.Name.ToString(), item => item.Value.ToString(), StringComparer.Ordinal);
        if (!long.TryParse(values.GetValueOrDefault("detectedAt"), out var detectedAt) || !long.TryParse(values.GetValueOrDefault("lastObservedAt"), out var lastObservedAt)
            || !long.TryParse(values.GetValueOrDefault("requestCount"), out var count) || !long.TryParse(values.GetValueOrDefault("windowSeconds"), out var windowSeconds)) return null;
        var contextReads = await Task.WhenAll(database.SetMembersAsync(IncidentUsersKey(incidentId)), database.SetMembersAsync(IncidentApplicationsKey(incidentId)), database.SetMembersAsync(IncidentHostsKey(incidentId))).WaitAsync(cancellationToken);
        var users = contextReads[0].Select(value => Guid.TryParse(value.ToString(), out var parsed) ? parsed : (Guid?)null).Where(value => value is not null).Select(value => value!.Value).Order().ToArray();
        return new SuspiciousRequestEvent(DateTimeOffset.FromUnixTimeMilliseconds(detectedAt), DateTimeOffset.FromUnixTimeMilliseconds(lastObservedAt), values.GetValueOrDefault("address") ?? "Unavailable",
            count, TimeSpan.FromSeconds(windowSeconds), users, contextReads[1].Select(value => value.ToString()).Order(StringComparer.Ordinal).ToArray(), contextReads[2].Select(value => value.ToString()).Order(StringComparer.Ordinal).ToArray());
    }

    private static string ObservationMember(SuspiciousRequestObservation observation)
        => string.Join('|', Guid.NewGuid().ToString("N"), observation.UserId?.ToString("N") ?? string.Empty, Encode(observation.Application), Encode(observation.Host), Encode(observation.ClientIpAddress), observation.TimestampUtc.ToUnixTimeMilliseconds());

    private static SuspiciousRequestObservation? ParseObservation(string value)
    {
        var parts = value.Split('|');
        if (parts.Length != 6 || !long.TryParse(parts[5], out var timestamp)) return null;
        Guid? userId = Guid.TryParseExact(parts[1], "N", out var parsed) ? parsed : null;
        return new SuspiciousRequestObservation(DateTimeOffset.FromUnixTimeMilliseconds(timestamp), Decode(parts[4]), userId, Decode(parts[2]), Decode(parts[3]));
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    private static string AddressKey(string address) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));
    private static RedisKey RollingKey(string addressKey) => $"{KeyPrefix}rolling:{addressKey}";
    private static RedisKey ActiveIncidentKey(string addressKey) => $"{KeyPrefix}active:{addressKey}";
    private static RedisKey IncidentsKey() => $"{KeyPrefix}incidents";
    private static string IncidentKeyPrefix() => $"{KeyPrefix}incident:";
    private static RedisKey IncidentKey(string incidentId) => $"{IncidentKeyPrefix()}{incidentId}";
    private static RedisKey IncidentUsersKey(string incidentId) => $"{IncidentKey(incidentId)}:users";
    private static RedisKey IncidentApplicationsKey(string incidentId) => $"{IncidentKey(incidentId)}:applications";
    private static RedisKey IncidentHostsKey(string incidentId) => $"{IncidentKey(incidentId)}:hosts";

    private void LogFailure(Exception exception, string message)
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref lastFailureLogUtcTicks);
        if (now - previous < TimeSpan.FromMinutes(1).Ticks || Interlocked.CompareExchange(ref lastFailureLogUtcTicks, now, previous) != previous) return;
        logger.LogWarning(exception, message);
    }
}
