using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class AuthenticationAuditEventConfiguration : IEntityTypeConfiguration<AuthenticationAuditEvent>
{
    public void Configure(EntityTypeBuilder<AuthenticationAuditEvent> builder)
    {
        builder.ToTable("authentication_audit_events");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.EventType).HasMaxLength(80).IsRequired();
        builder.Property(item => item.Outcome).HasMaxLength(30).IsRequired();
        builder.Property(item => item.FailureReason).HasMaxLength(80);
        builder.Property(item => item.NormalizedUsername).HasMaxLength(256);
        builder.Property(item => item.RemoteIpAddress).HasMaxLength(64);
        builder.Property(item => item.ForwardedClientIpAddress).HasMaxLength(64);
        builder.Property(item => item.UserAgent).HasMaxLength(512);
        builder.Property(item => item.RequestPath).HasMaxLength(512).IsRequired();
        builder.Property(item => item.HttpMethod).HasMaxLength(16).IsRequired();
        builder.Property(item => item.CorrelationId).HasMaxLength(128);
        builder.Property(item => item.SessionFingerprint).HasMaxLength(128);
        builder.Property(item => item.Metadata).HasMaxLength(2000);
        builder.HasOne(item => item.User).WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(item => new { item.UserId, item.TimestampUtc });
        builder.HasIndex(item => new { item.NormalizedUsername, item.TimestampUtc });
    }
}
