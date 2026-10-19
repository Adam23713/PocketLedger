using Microsoft.EntityFrameworkCore;

namespace PocketLedger.Web.Data;

public sealed class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options)
{
    public DbSet<BffSession> Sessions => Set<BffSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<BffSession>(entity =>
        {
            entity.ToTable("bff_sessions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(200);
            entity.Property(item => item.ProtectedTicket).HasColumnName("protected_ticket").IsRequired();
            entity.Property(item => item.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(item => item.UserId).HasColumnName("user_id");
            entity.Property(item => item.RevokedAtUtc).HasColumnName("revoked_at_utc");
            entity.HasIndex(item => item.ExpiresAtUtc);
            entity.HasIndex(item => item.UserId);
        });
    }
}

public sealed class BffSession
{
    public string Id { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Guid? UserId { get; set; }
    // Set when an administrator blocks the user. The ticket (including its tokens) is wiped at that moment;
    // the row only remains so the next request from that browser can be shown the "access blocked" page.
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
