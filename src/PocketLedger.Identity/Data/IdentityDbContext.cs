using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Data;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, DatabaseEncryption? encryption = null) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IEncryptedDbContext
{
    public DatabaseEncryption? Encryption => encryption;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.ReplaceService<IModelCacheKeyFactory, EncryptionModelCacheKeyFactory>();

    public DbSet<AuthenticationAuditEvent> AuthenticationAuditEvents => Set<AuthenticationAuditEvent>();
    public DbSet<DeletedUserTombstone> DeletedUserTombstones => Set<DeletedUserTombstone>();
    public DbSet<RegistrationSettings> RegistrationSettings => Set<RegistrationSettings>();
    public DbSet<UserDataKeyRecord> UserDataKeys => Set<UserDataKeyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
        builder.Entity<IdentityUserToken<Guid>>().Property(item => item.Value).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<IdentityUserToken<Guid>>().Property(item => item.Value), "IdentityUserToken.Value");
        builder.Entity<ApplicationUser>().Property(item => item.PhoneNumber).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<ApplicationUser>().Property(item => item.PhoneNumber), "ApplicationUser.PhoneNumber");
        builder.Entity<ApplicationUser>().Property(item => item.LastSuccessfulLoginIpAddress).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<ApplicationUser>().Property(item => item.LastSuccessfulLoginIpAddress), "ApplicationUser.LastSuccessfulLoginIpAddress");
        builder.Entity<ApplicationUser>().HasIndex(item => item.NormalizedEmail).IsUnique().HasFilter("\"NormalizedEmail\" IS NOT NULL");
        builder.Entity<ApplicationUser>().HasIndex(item => item.IsBootstrapAdministrator).IsUnique().HasFilter("\"IsBootstrapAdministrator\" = TRUE");
        builder.Entity<DeletedUserTombstone>(entity =>
        {
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.DeletedAtUtc).IsRequired();
        });
        builder.Entity<RegistrationSettings>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.ToTable(table => table.HasCheckConstraint("CK_RegistrationSettings_Singleton", "\"Id\" = 1"));
        });
        builder.Entity<UserDataKeyRecord>(entity =>
        {
            entity.ToTable("UserDataKeys");
            entity.HasKey(item => item.UserId);
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserDataKeyRecord>(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(item => item.PasswordWrap).HasColumnType("text").IsRequired();
            entity.Property(item => item.RecoveryWrap).HasColumnType("text").IsRequired();
        });
        encryption?.Configure(builder.Entity<UserDataKeyRecord>().Property(item => item.PasswordWrap), "UserDataKey.PasswordWrap");
        encryption?.Configure(builder.Entity<UserDataKeyRecord>().Property(item => item.RecoveryWrap), "UserDataKey.RecoveryWrap");
        builder.Entity<AuthenticationAuditEvent>().Property(item => item.RemoteIpAddress).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<AuthenticationAuditEvent>().Property(item => item.RemoteIpAddress), "AuthenticationAuditEvent.RemoteIpAddress", 64);
        builder.Entity<AuthenticationAuditEvent>().Property(item => item.ForwardedClientIpAddress).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<AuthenticationAuditEvent>().Property(item => item.ForwardedClientIpAddress), "AuthenticationAuditEvent.ForwardedClientIpAddress", 64);
        builder.Entity<AuthenticationAuditEvent>().Property(item => item.UserAgent).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<AuthenticationAuditEvent>().Property(item => item.UserAgent), "AuthenticationAuditEvent.UserAgent", 512);
        builder.Entity<AuthenticationAuditEvent>().Property(item => item.Metadata).HasColumnType("text").Metadata.SetMaxLength(null);
        encryption?.Configure(builder.Entity<AuthenticationAuditEvent>().Property(item => item.Metadata), "AuthenticationAuditEvent.Metadata", 2000);
    }
}
