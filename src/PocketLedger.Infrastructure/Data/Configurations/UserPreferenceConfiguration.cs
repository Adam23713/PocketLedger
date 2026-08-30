using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> builder)
    {
        builder.ToTable("user_preferences");
        builder.HasKey(item => item.UserId);
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        builder.Property(item => item.AvatarId).HasColumnName("avatar_id");
        builder.Property(item => item.DefaultCurrency).HasColumnName("default_currency").HasMaxLength(3).IsFixedLength();
        builder.Property(item => item.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100);
    }
}
