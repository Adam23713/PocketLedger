using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class DebtConfiguration : IEntityTypeConfiguration<Debt>
{
    public void Configure(EntityTypeBuilder<Debt> builder)
    {
        builder.ToTable("debts", table =>
        {
            table.HasCheckConstraint("ck_debts_original_amount_positive", "original_amount > 0");
            table.HasCheckConstraint("ck_debts_date_range", "due_date IS NULL OR due_date >= start_date");
            table.HasCheckConstraint("ck_debts_receivable_type", "direction <> 'Receivable' OR type = 'PrivatePerson'");
        });
        builder.HasKey(debt => debt.Id).HasName("pk_debts");
        builder.Property(debt => debt.Id).HasColumnName("id");
        builder.Property(debt => debt.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(debt => debt.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(debt => debt.Icon).HasColumnName("icon").HasMaxLength(100).IsRequired();
        builder.Property(debt => debt.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(debt => debt.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(debt => debt.CounterpartyName).HasColumnName("counterparty_name").HasMaxLength(200).IsRequired();
        builder.Property(debt => debt.OriginalAmount).HasColumnName("original_amount").HasPrecision(19, 4);
        builder.Property(debt => debt.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(debt => debt.StartDate).HasColumnName("start_date").HasColumnType("date");
        builder.Property(debt => debt.DueDate).HasColumnName("due_date").HasColumnType("date");
        builder.Property(debt => debt.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(debt => debt.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(debt => debt.ClosedAt).HasColumnName("closed_at");
        builder.Property(debt => debt.AccountId).HasColumnName("account_id");
        builder.HasOne(debt => debt.Account).WithMany().HasForeignKey(debt => debt.AccountId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_debts_accounts_account_id");
        builder.HasIndex(debt => new { debt.OwnerId, debt.Status }).HasDatabaseName("ix_debts_owner_id_status");
        builder.HasIndex(debt => debt.AccountId).HasDatabaseName("ix_debts_account_id");
    }
}
