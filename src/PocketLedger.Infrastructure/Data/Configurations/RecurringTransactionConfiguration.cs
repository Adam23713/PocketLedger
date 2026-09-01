using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class RecurringTransactionConfiguration : IEntityTypeConfiguration<RecurringTransaction>
{
    public void Configure(EntityTypeBuilder<RecurringTransaction> builder)
    {
        builder.ToTable("recurring_transactions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_recurring_transactions_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint("ck_recurring_transactions_date_range", "last_occurrence IS NULL OR last_occurrence >= first_occurrence");
            tableBuilder.HasCheckConstraint("ck_recurring_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
            tableBuilder.HasCheckConstraint("ck_recurring_transactions_category", "(debt_id IS NOT NULL AND category_id IS NULL) OR (debt_id IS NULL AND type IN ('Income', 'Expense') AND category_id IS NOT NULL) OR (debt_id IS NULL AND type = 'Adjustment' AND category_id IS NULL)");
        });

        builder.HasKey(template => template.Id).HasName("pk_recurring_transactions");
        builder.Property(template => template.Id).HasColumnName("id");
        builder.Property(template => template.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(template => template.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(template => template.AccountId).HasColumnName("account_id");
        builder.Property(template => template.CategoryId).HasColumnName("category_id");
        builder.Property(template => template.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(template => template.AdjustmentDirection).HasColumnName("adjustment_direction").HasConversion<string>().HasMaxLength(10);
        builder.Property(template => template.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(template => template.FirstOccurrence).HasColumnName("first_occurrence").HasColumnType("date");
        builder.Property(template => template.LastOccurrence).HasColumnName("last_occurrence").HasColumnType("date");
        builder.Property(template => template.AutomationStartsOn).HasColumnName("automation_starts_on").HasColumnType("date").HasDefaultValueSql("(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Budapest')::date");
        builder.Property(template => template.Frequency).HasColumnName("frequency").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(template => template.Enabled).HasColumnName("enabled");
        builder.Property(template => template.DebtId).HasColumnName("debt_id");
        builder.Property(template => template.DebtOperationType).HasColumnName("debt_operation_type").HasConversion<string>().HasMaxLength(40);

        builder.HasOne(template => template.Account).WithMany().HasForeignKey(template => template.AccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_recurring_transactions_accounts_account_id");
        builder.HasOne(template => template.Category).WithMany().HasForeignKey(template => template.CategoryId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_recurring_transactions_categories_category_id");
        builder.HasOne(template => template.Debt).WithMany(debt => debt.RecurringTransactions).HasForeignKey(template => template.DebtId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_recurring_transactions_debts_debt_id");

        builder.HasIndex(template => template.AccountId).HasDatabaseName("ix_recurring_transactions_account_id");
        builder.HasIndex(template => template.CategoryId).HasDatabaseName("ix_recurring_transactions_category_id");
        builder.HasIndex(template => new { template.Enabled, template.FirstOccurrence }).HasDatabaseName("ix_recurring_transactions_enabled_first_occurrence");
        builder.HasIndex(template => new { template.OwnerId, template.Enabled, template.FirstOccurrence }).HasDatabaseName("ix_recurring_transactions_owner_id_enabled_first_occurrence");
        builder.HasIndex(template => template.DebtId).HasDatabaseName("ix_recurring_transactions_debt_id");
    }
}
