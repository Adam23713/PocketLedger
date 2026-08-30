namespace PocketLedger.Models.Entities;

public class AuthenticationAuditEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public Guid? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public string? NormalizedUsername { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public string? RemoteIpAddress { get; set; }
    public string? ForwardedClientIpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string RequestPath { get; set; } = string.Empty;
    public string HttpMethod { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? SessionFingerprint { get; set; }
    public string? Metadata { get; set; }
}
