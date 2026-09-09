using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class PlannerMonthRecordConfiguration : IEntityTypeConfiguration<PlannerMonthRecord>
{
    public void Configure(EntityTypeBuilder<PlannerMonthRecord> builder)
    {
        builder.ToTable("planner_months");
        builder.HasKey(item => item.Id).HasName("pk_planner_months");
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(item => item.Month).HasColumnName("month").HasColumnType("date");
        builder.Property(item => item.IsClosed).HasColumnName("is_closed");
        builder.Property(item => item.OpeningBalancesJson).HasColumnName("opening_balances_json").HasColumnType("text");
        builder.Property(item => item.SnapshotJson).HasColumnName("snapshot_json").HasColumnType("text");
        builder.HasIndex(item => new { item.OwnerId, item.Month }).IsUnique().HasDatabaseName("ix_planner_months_owner_id_month");
    }
}
