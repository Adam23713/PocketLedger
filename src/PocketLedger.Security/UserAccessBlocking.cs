using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace PocketLedger.Security;

public interface IUserAccessBlockStore
{
    Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default);
    Task SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default);
    Task InitializeIfMissingAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);
}

public enum UserAccessBlockState
{
    Active,
    Blocked,
    Unavailable
}

public static class UserAccessBlockingRegistration
{
    public static IServiceCollection AddUserAccessBlocking(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration["AccessControl:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString)) services.AddSingleton<IUserAccessBlockStore, DisabledUserAccessBlockStore>();
        else services.AddSingleton<IUserAccessBlockStore>(provider => new RedisUserAccessBlockStore(connectionString, provider.GetRequiredService<ILogger<RedisUserAccessBlockStore>>()));
        return services;
    }

    public static IApplicationBuilder UseUserAccessBlocking(this IApplicationBuilder app, string? blockedPath = null)
        => blockedPath is null ? app.UseMiddleware<UserAccessBlockMiddleware>() : app.UseMiddleware<UserAccessBlockMiddleware>(blockedPath);
}

internal sealed class DisabledUserAccessBlockStore : IUserAccessBlockStore
{
    public Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(UserAccessBlockState.Active);
    public Task SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task InitializeIfMissingAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class RedisUserAccessBlockStore(string connectionString, ILogger<RedisUserAccessBlockStore> logger) : IUserAccessBlockStore, IDisposable
{
    private const string StateKey = "pocketledger:access-control:state";
    private const string InitializedField = "__initialized";
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private IConnectionMultiplexer? connection;

    public async Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var database = (await GetConnectionAsync(cancellationToken)).GetDatabase();
            if (!await database.HashExistsAsync(StateKey, InitializedField)) return UserAccessBlockState.Unavailable;
            return await database.HashExistsAsync(StateKey, userId.ToString("N")) ? UserAccessBlockState.Blocked : UserAccessBlockState.Active;
        }
        catch (RedisException exception)
        {
            logger.LogError(exception, "User access block state is unavailable.");
            return UserAccessBlockState.Unavailable;
        }
    }

    public async Task SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken = default)
    {
        var database = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        if (blocked) await database.HashSetAsync(StateKey, userId.ToString("N"), "1");
        else await database.HashDeleteAsync(StateKey, userId.ToString("N"));
        await database.HashSetAsync(StateKey, InitializedField, "1");
    }

    public async Task InitializeIfMissingAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        var database = (await GetConnectionAsync(cancellationToken)).GetDatabase();
        var transaction = database.CreateTransaction();
        transaction.AddCondition(Condition.KeyNotExists(StateKey));
        var entries = userIds.Select(userId => new HashEntry(userId.ToString("N"), "1")).Append(new HashEntry(InitializedField, "1")).ToArray();
        _ = transaction.HashSetAsync(StateKey, entries);
        await transaction.ExecuteAsync();
    }

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (connection is { IsConnected: true }) return connection;
        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (connection is { IsConnected: true }) return connection;
            connection?.Dispose();
            var configuration = ConfigurationOptions.Parse(connectionString);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 1000;
            configuration.AsyncTimeout = 1000;
            configuration.ConnectRetry = 1;
            connection = await ConnectionMultiplexer.ConnectAsync(configuration);
            return connection;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public void Dispose()
    {
        connection?.Dispose();
        connectionLock.Dispose();
    }
}

public sealed class UserAccessBlockMiddleware(RequestDelegate next, string? blockedPath = null)
{
    public async Task InvokeAsync(HttpContext context, IUserAccessBlockStore store)
    {
        if (TryGetUserId(context.User, out var userId))
        {
            var state = await store.IsBlockedAsync(userId, context.RequestAborted);
            if (state == UserAccessBlockState.Unavailable)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("User access validation is temporarily unavailable.", context.RequestAborted);
                return;
            }
            if (state == UserAccessBlockState.Blocked && (blockedPath is null || !context.Request.Path.Equals(blockedPath, StringComparison.OrdinalIgnoreCase)))
            {
                if (blockedPath is not null)
                {
                    context.Response.Redirect(blockedPath);
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { Error = "Access to this account is currently blocked and under review." }, context.RequestAborted);
                return;
            }
        }
        await next(context);
    }

    private static bool TryGetUserId(System.Security.Claims.ClaimsPrincipal principal, out Guid userId)
    {
        var value = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value, out userId);
    }
}
