using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace PocketLedger.Web.Data;

public sealed class WebDbContext(DbContextOptions<WebDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<BffSession> Sessions => Set<BffSession>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<BffSession>(entity =>
        {
            entity.ToTable("bff_sessions");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("id").HasMaxLength(200);
            entity.Property(item => item.ProtectedTicket).HasColumnName("protected_ticket").IsRequired();
            entity.Property(item => item.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.HasIndex(item => item.ExpiresAtUtc);
        });
    }
}

public sealed class BffSession
{
    public string Id { get; set; } = string.Empty;
    public byte[] ProtectedTicket { get; set; } = [];
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
