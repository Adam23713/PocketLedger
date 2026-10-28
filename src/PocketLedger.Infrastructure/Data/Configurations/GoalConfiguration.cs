using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goals", table =>
        {
            table.HasCheckConstraint("ck_goals_amounts", "target_amount > 0 AND saved_amount >= 0");
        });
        builder.HasKey(item => item.Id).HasName("pk_goals");
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(item => item.Name).HasColumnName("name").HasColumnType("text");
        builder.Property(item => item.TargetAmount).HasColumnName("target_amount").HasPrecision(19, 4);
        builder.Property(item => item.SavedAmount).HasColumnName("saved_amount").HasPrecision(19, 4);
        builder.Property(item => item.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(item => item.TargetDate).HasColumnName("target_date").HasColumnType("date");
        builder.Property(item => item.BannerColor).HasColumnName("banner_color").HasMaxLength(7);
        builder.Property(item => item.BannerImage).HasColumnName("banner_image").HasMaxLength(50);
        builder.Property(item => item.Note).HasColumnName("note").HasColumnType("text");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(item => item.OwnerId).HasDatabaseName("ix_goals_owner_id");
    }
}
