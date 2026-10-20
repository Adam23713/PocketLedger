using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public sealed class BlockedUserConfiguration : IEntityTypeConfiguration<BlockedUser>
{
    public void Configure(EntityTypeBuilder<BlockedUser> builder)
    {
        builder.ToTable("blocked_users");
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(item => item.BlockedAtUtc).HasColumnName("blocked_at_utc");
    }
}
