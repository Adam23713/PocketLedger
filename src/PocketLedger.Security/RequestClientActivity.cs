using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PocketLedger.Security;

public sealed record RequestClientSummary(string ClientIpAddress, long TotalRequests, long FailedRequests, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

public sealed record RequestClientBucket(DateTimeOffset StartedAtUtc, long FailedRequests, int DistinctClients, string? TopClientIpAddress, long TopClientRequests, long SelectedClientRequests, long SelectedClientFailedRequests);

public sealed record RequestClientActivityResult(bool IsAvailable, int TotalClients, long OmittedRequests, IReadOnlyList<RequestClientSummary> Clients, IReadOnlyList<RequestClientBucket> Buckets);

public sealed record RequestClientEndpoint(string Method, string Path, string Application, int StatusCode, long Requests, DateTimeOffset LastSeenHourUtc);

public sealed record RequestClientDetails(bool IsAvailable, string ClientIpAddress, long OmittedRequests, bool IsPossiblyIncomplete, IReadOnlyList<RequestClientEndpoint> Endpoints, IReadOnlyList<Guid> UserIds);

public interface IRequestClientActivityReader
{
    Task<RequestClientActivityResult> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? selectedClientIpAddress = null, CancellationToken cancellationToken = default);
    Task<RequestClientDetails> GetDetailsAsync(string clientIpAddress, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

public static class RequestClientAddress
{
    // IPAddress.TryParse also accepts shorthand such as "1.2.3" (1.2.0.3), which would silently select a different address than the one typed.
    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || !IPAddress.TryParse(text, out var address)) return false;
        if (address.AddressFamily == AddressFamily.InterNetwork && text.Count(character => character == '.') != 3) return false;
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0) return false;
        normalized = Normalize(address);
        return true;
    }

    public static string Normalize(IPAddress address) => (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}

internal static class RequestEndpointDescription
{
    public const int MaximumPathLength = 120;

    // Identifiers are collapsed so one endpoint stays one row and a stored path never holds a record's ID. Query strings are never passed in.
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        var segments = path.Split('/').Select(segment => IsIdentifier(segment) ? "{id}" : segment);
        var normalized = string.Concat(string.Join('/', segments).Select(character => char.IsControl(character) ? '?' : character));
        return normalized.Length <= MaximumPathLength ? normalized : normalized[..MaximumPathLength] + "…";
    }

    // HTTP allows arbitrary method tokens, including the field separator used in storage.
    public static string NormalizeMethod(string method) => method.ToUpperInvariant() switch
    {
        "GET" => "GET", "POST" => "POST", "PUT" => "PUT", "PATCH" => "PATCH", "DELETE" => "DELETE", "HEAD" => "HEAD", "OPTIONS" => "OPTIONS",
        _ => "OTHER"
    };

    public static string ShortenApplication(string application) => application.StartsWith("PocketLedger.", StringComparison.Ordinal) ? application["PocketLedger.".Length..] : application;

    private static bool IsIdentifier(string segment) => segment.Length > 0 && (segment.All(char.IsAsciiDigit) || Guid.TryParse(segment, out _));
}

// Answers "who sent this traffic and what did they ask for": per-address counters in five-minute buckets, and per-address endpoints in hourly buckets.
// It shares the noeviction security Valkey with suspicious request detection, so both key families have hard field limits that keep a flood of
// random addresses or paths from filling the instance; see docs/deployment.md for the resulting memory bound.
internal sealed class RequestClientActivityService(IOptions<RequestTelemetryOptions> options, RequestTelemetryConnection store, ILogger<RequestClientActivityService> logger) : IRequestClientActivityReader
{
    internal const int MaximumClientFieldsPerBucket = 240;
    internal const int MaximumEndpointFieldsPerHour = 1000;
    internal const int MaximumEndpointFieldsPerClient = 200;
    private const int EndpointBucketMinutes = 60;
    private const int MaximumReturnedClients = 200;
    private const int MaximumReturnedEndpoints = 500;
    private const string ClientsKeyPrefix = "pocketledger:request-clients:v1:";
    private const string EndpointsKeyPrefix = "pocketledger:request-endpoints:v1:";
    private const string OmittedField = "omitted";

    private const string ClientScript = """
        local requests = 'r:' .. ARGV[1]
        if redis.call('HEXISTS', KEYS[1], requests) == 0 and redis.call('HLEN', KEYS[1]) >= tonumber(ARGV[5]) then
            redis.call('HINCRBY', KEYS[1], 'omitted', ARGV[2])
        else
            redis.call('HINCRBY', KEYS[1], requests, ARGV[2])
            if tonumber(ARGV[3]) > 0 then redis.call('HINCRBY', KEYS[1], 'f:' .. ARGV[1], ARGV[3]) end
        end
        return redis.call('PEXPIREAT', KEYS[1], ARGV[4])
        """;

    // The per-address limit keeps one scanner working through a word list from using up the hour's whole budget and hiding every other address.
    private const string EndpointScript = """
        local field, address, count = ARGV[1], ARGV[2], ARGV[3]
        if redis.call('HEXISTS', KEYS[1], field) == 1 then
            redis.call('HINCRBY', KEYS[1], field, count)
        elseif redis.call('HLEN', KEYS[1]) >= tonumber(ARGV[4]) then
            redis.call('HINCRBY', KEYS[1], 'omitted', count)
        else
            local distinct = 'n|' .. address .. '|'
            if tonumber(redis.call('HGET', KEYS[1], distinct) or '0') >= tonumber(ARGV[5]) then
                redis.call('HINCRBY', KEYS[1], 'o|' .. address .. '|', count)
            else
                redis.call('HINCRBY', KEYS[1], distinct, 1)
                redis.call('HINCRBY', KEYS[1], field, count)
            end
        end
        return redis.call('PEXPIREAT', KEYS[1], ARGV[6])
        """;

    private long lastFailureLogUtcTicks;

    public IEnumerable<Task> Write(IDatabase database, IReadOnlyCollection<RequestTelemetryObservation> observations)
    {
        var attributed = observations.Where(item => item.ClientIpAddress is not null).ToArray();
        var writes = new List<Task>();
        foreach (var group in attributed.GroupBy(item => (item.BucketStartUtc, Address: item.ClientIpAddress!)))
        {
            var expiresAt = group.Key.BucketStartUtc.AddHours(options.Value.RetentionHours).ToUnixTimeMilliseconds();
            writes.Add(database.ScriptEvaluateAsync(ClientScript, [ClientsKey(group.Key.BucketStartUtc)],
                [group.Key.Address, group.LongCount(), group.LongCount(item => item.StatusCode >= 400), expiresAt, MaximumClientFieldsPerBucket]));
        }

        foreach (var hour in attributed.GroupBy(item => (Start: RequestTelemetryBuckets.Start(item.BucketStartUtc, EndpointBucketMinutes), Address: item.ClientIpAddress!)))
        {
            var key = EndpointsKey(hour.Key.Start);
            var expiresAt = hour.Key.Start.AddHours(options.Value.RetentionHours).ToUnixTimeMilliseconds();
            var fields = hour.GroupBy(item => EndpointField(item)).Select(group => (Field: group.Key, Count: group.LongCount()))
                .Concat(hour.Where(item => item.UserId is not null).GroupBy(item => UserField(item.ClientIpAddress!, item.UserId!.Value)).Select(group => (Field: group.Key, Count: group.LongCount())));
            foreach (var (field, count) in fields)
                writes.Add(database.ScriptEvaluateAsync(EndpointScript, [key], [field, hour.Key.Address, count, MaximumEndpointFieldsPerHour, MaximumEndpointFieldsPerClient, expiresAt]));
        }
        return writes;
    }

    public async Task<RequestClientActivityResult> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? selectedClientIpAddress = null, CancellationToken cancellationToken = default)
    {
        var starts = RequestTelemetryBuckets.Range(fromUtc, toUtc, options.Value.BucketMinutes);
        var unavailable = new RequestClientActivityResult(false, 0, 0, [], starts.Select(start => new RequestClientBucket(start, 0, 0, null, 0, 0, 0)).ToArray());
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString)) return unavailable;
        try
        {
            var database = await store.GetDatabaseAsync(cancellationToken);
            var reads = starts.Select(start => database.HashGetAllAsync(ClientsKey(start))).ToArray();
            await Task.WhenAll(reads).WaitAsync(cancellationToken);

            var clients = new Dictionary<string, (long Total, long Failed, DateTimeOffset First, DateTimeOffset Last)>(StringComparer.Ordinal);
            var buckets = new RequestClientBucket[starts.Count];
            var omitted = 0L;
            for (var index = 0; index < starts.Count; index++)
            {
                var start = starts[index];
                var requests = new Dictionary<string, long>(StringComparer.Ordinal);
                var failures = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var entry in reads[index].Result)
                {
                    var name = entry.Name.ToString();
                    var value = (long)entry.Value;
                    if (name == OmittedField) omitted += value;
                    else if (name.StartsWith("r:", StringComparison.Ordinal)) requests[name[2..]] = value;
                    else if (name.StartsWith("f:", StringComparison.Ordinal)) failures[name[2..]] = value;
                }
                foreach (var (address, count) in requests)
                {
                    var failed = failures.GetValueOrDefault(address);
                    clients[address] = clients.TryGetValue(address, out var existing) ? (existing.Total + count, existing.Failed + failed, existing.First, start) : (count, failed, start, start);
                }
                var top = requests.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal).FirstOrDefault();
                buckets[index] = new RequestClientBucket(start, failures.Values.Sum(), requests.Count, top.Key, top.Value,
                    selectedClientIpAddress is null ? 0 : requests.GetValueOrDefault(selectedClientIpAddress), selectedClientIpAddress is null ? 0 : failures.GetValueOrDefault(selectedClientIpAddress));
            }

            var summaries = clients.OrderByDescending(item => item.Value.Total).ThenBy(item => item.Key, StringComparer.Ordinal).Take(MaximumReturnedClients)
                .Select(item => new RequestClientSummary(item.Key, item.Value.Total, item.Value.Failed, item.Value.First, item.Value.Last)).ToArray();
            return new RequestClientActivityResult(true, clients.Count, omitted, summaries, buckets);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Request client activity could not be read.");
            return unavailable;
        }
    }

    public async Task<RequestClientDetails> GetDetailsAsync(string clientIpAddress, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        if (!RequestClientAddress.TryNormalize(clientIpAddress, out var address)) throw new ArgumentException("A complete IP address is required.", nameof(clientIpAddress));
        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString)) return new RequestClientDetails(false, address!, 0, false, [], []);
        try
        {
            var database = await store.GetDatabaseAsync(cancellationToken);
            var hours = RequestTelemetryBuckets.Range(fromUtc, toUtc, EndpointBucketMinutes);
            var reads = await Task.WhenAll(hours.Select(hour => ReadHourAsync(database, hour, address!, cancellationToken)));

            var endpoints = new Dictionary<(string Method, string Path, string Application, int Status), (long Requests, DateTimeOffset LastSeen)>();
            var users = new HashSet<Guid>();
            var omitted = 0L;
            var isPossiblyIncomplete = false;
            foreach (var (hour, entries, hourOmitted) in reads)
            {
                isPossiblyIncomplete |= hourOmitted > 0;
                foreach (var entry in entries)
                {
                    var parts = entry.Name.ToString().Split('|', 6);
                    switch (parts[0])
                    {
                        case "o":
                            omitted += (long)entry.Value;
                            break;
                        case "u" when parts.Length >= 3 && Guid.TryParseExact(parts[2], "N", out var userId):
                            users.Add(userId);
                            break;
                        case "d" when parts.Length == 6 && int.TryParse(parts[3], CultureInfo.InvariantCulture, out var status):
                            var key = (parts[2], parts[5], parts[4], status);
                            var count = (long)entry.Value;
                            endpoints[key] = endpoints.TryGetValue(key, out var existing) ? (existing.Requests + count, hour > existing.LastSeen ? hour : existing.LastSeen) : (count, hour);
                            break;
                    }
                }
            }

            var rows = endpoints.OrderByDescending(item => item.Value.Requests).ThenBy(item => item.Key.Path, StringComparer.Ordinal).Take(MaximumReturnedEndpoints)
                .Select(item => new RequestClientEndpoint(item.Key.Method, item.Key.Path, item.Key.Application, item.Key.Status, item.Value.Requests, item.Value.LastSeen)).ToArray();
            return new RequestClientDetails(true, address!, omitted, isPossiblyIncomplete, rows, users.Order().ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogFailure(exception, "Request client details could not be read.");
            return new RequestClientDetails(false, address!, 0, false, [], []);
        }
    }

    // The address is a normalized IP, so it cannot contain glob characters; the separators on both sides keep 1.2.3.4 from matching 1.2.3.45.
    private static async Task<(DateTimeOffset Hour, List<HashEntry> Entries, long Omitted)> ReadHourAsync(IDatabase database, DateTimeOffset hour, string address, CancellationToken cancellationToken)
    {
        var key = EndpointsKey(hour);
        var entries = new List<HashEntry>();
        await foreach (var entry in database.HashScanAsync(key, $"?|{address}|*", pageSize: 1000).WithCancellation(cancellationToken)) entries.Add(entry);
        var omitted = await database.HashGetAsync(key, OmittedField);
        return (hour, entries, omitted.HasValue ? (long)omitted : 0);
    }

    private static string EndpointField(RequestTelemetryObservation item)
        => string.Create(CultureInfo.InvariantCulture, $"d|{item.ClientIpAddress}|{item.Method}|{item.StatusCode}|{item.Application}|{item.Path}");

    private static string UserField(string address, Guid userId) => $"u|{address}|{userId:N}";
    private static RedisKey ClientsKey(DateTimeOffset bucketStart) => $"{ClientsKeyPrefix}{bucketStart.ToUnixTimeSeconds()}";
    private static RedisKey EndpointsKey(DateTimeOffset hourStart) => $"{EndpointsKeyPrefix}{hourStart.ToUnixTimeSeconds()}";

    private void LogFailure(Exception exception, string message)
    {
        var now = DateTime.UtcNow.Ticks;
        var previous = Interlocked.Read(ref lastFailureLogUtcTicks);
        if (now - previous < TimeSpan.FromMinutes(1).Ticks || Interlocked.CompareExchange(ref lastFailureLogUtcTicks, now, previous) != previous) return;
        logger.LogWarning(exception, message);
    }
}
