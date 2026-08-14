using System.Security.Cryptography;
using System.Text;
using PocketLedger.Data;
using PocketLedger.Models.Entities;

namespace PocketLedger.Services;

public interface IAuthenticationAuditService
{
    Task WriteAsync(string eventType, string outcome, Guid? userId = null, string? normalizedUsername = null, string? failureReason = null, string? metadata = null, CancellationToken cancellationToken = default);
}

public sealed class AuthenticationAuditService(PocketLedgerDbContext dbContext, IHttpContextAccessor contextAccessor, IClientIpAddressResolver clientIpAddressResolver, ILogger<AuthenticationAuditService> logger) : IAuthenticationAuditService
{
    public async Task WriteAsync(string eventType, string outcome, Guid? userId = null, string? normalizedUsername = null, string? failureReason = null, string? metadata = null, CancellationToken cancellationToken = default)
    {
        var context = contextAccessor.HttpContext;
        var sessionSource = context?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value + "|" + context?.TraceIdentifier;
        var item = new AuthenticationAuditEvent
        {
            Id = Guid.NewGuid(), TimestampUtc = DateTimeOffset.UtcNow, UserId = userId, NormalizedUsername = normalizedUsername,
            EventType = eventType, Outcome = outcome, FailureReason = failureReason,
            RemoteIpAddress = context?.Connection.RemoteIpAddress?.ToString(),
            ForwardedClientIpAddress = context is null ? null : clientIpAddressResolver.GetForwardedClientIpAddress(context),
            UserAgent = context?.Request.Headers.UserAgent.ToString(), RequestPath = context?.Request.Path.Value ?? string.Empty,
            HttpMethod = context?.Request.Method ?? string.Empty, CorrelationId = context?.TraceIdentifier,
            SessionFingerprint = string.IsNullOrEmpty(sessionSource) ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionSource))), Metadata = metadata
        };
        dbContext.AuthenticationAuditEvents.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Authentication audit {EventType} {Outcome} user {UserId} reason {FailureReason}", eventType, outcome, userId, failureReason);
    }
}
