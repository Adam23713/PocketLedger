using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class PlannerItemConfiguration : IEntityTypeConfiguration<PlannerItem>
{
    public void Configure(EntityTypeBuilder<PlannerItem> builder)
    {
        builder.ToTable("planner_items", table =>
        {
            table.HasCheckConstraint("ck_planner_items_amounts", "amount > 0 AND account_amount > 0 AND (target_amount IS NULL OR target_amount > 0)");
            table.HasCheckConstraint("ck_planner_items_type", "type IN ('Income', 'Expense', 'Transfer')");
            table.HasCheckConstraint("ck_planner_items_transfer", "(type = 'Transfer' AND target_account_id IS NOT NULL AND target_account_id <> account_id AND target_amount IS NOT NULL AND planned_date IS NOT NULL AND category_id IS NULL) OR (type <> 'Transfer' AND target_account_id IS NULL AND target_amount IS NULL AND category_id IS NOT NULL)");
        });
        builder.HasKey(item => item.Id).HasName("pk_planner_items");
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(item => item.Month).HasColumnName("month").HasColumnType("date");
        builder.Property(item => item.CopyDay).HasColumnName("copy_day");
        builder.Property(item => item.PlannedDate).HasColumnName("planned_date").HasColumnType("date");
        builder.Property(item => item.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10);
        builder.Property(item => item.AccountId).HasColumnName("account_id");
        builder.Property(item => item.TargetAccountId).HasColumnName("target_account_id");
        builder.Property(item => item.CategoryId).HasColumnName("category_id");
        builder.Property(item => item.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(item => item.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(item => item.AccountAmount).HasColumnName("account_amount").HasPrecision(19, 4);
        builder.Property(item => item.TargetAmount).HasColumnName("target_amount").HasPrecision(19, 4);
        builder.Property(item => item.Note).HasColumnName("note").HasMaxLength(500);
        builder.HasOne(item => item.Account).WithMany().HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_planner_items_accounts_account_id");
        builder.HasOne(item => item.TargetAccount).WithMany().HasForeignKey(item => item.TargetAccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_planner_items_accounts_target_account_id");
        builder.HasOne(item => item.Category).WithMany().HasForeignKey(item => item.CategoryId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_planner_items_categories_category_id");
        builder.HasIndex(item => item.AccountId).HasDatabaseName("ix_planner_items_account_id");
        builder.HasIndex(item => item.TargetAccountId).HasDatabaseName("ix_planner_items_target_account_id");
        builder.HasIndex(item => item.CategoryId).HasDatabaseName("ix_planner_items_category_id");
        builder.HasIndex(item => new { item.OwnerId, item.Month }).HasDatabaseName("ix_planner_items_owner_id_month");
    }
}
