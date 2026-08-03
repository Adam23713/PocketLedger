using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("transactions", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_transactions_amount_positive", "amount > 0");
            tableBuilder.HasCheckConstraint("ck_transactions_target_amount_positive", "target_amount IS NULL OR target_amount > 0");
            tableBuilder.HasCheckConstraint("ck_transactions_different_accounts", "target_account_id IS NULL OR target_account_id <> account_id");
            tableBuilder.HasCheckConstraint("ck_transactions_transfer_target", "(type = 'Transfer' AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");
            tableBuilder.HasCheckConstraint("ck_transactions_target_amount_transfer", "target_amount IS NULL OR type = 'Transfer'");
            tableBuilder.HasCheckConstraint("ck_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
        });

        builder.HasKey(transaction => transaction.Id).HasName("pk_transactions");
        builder.Property(transaction => transaction.Id).HasColumnName("id");
        builder.Property(transaction => transaction.OwnerId).HasColumnName("owner_id");
        builder.Property(transaction => transaction.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(transaction => transaction.AccountId).HasColumnName("account_id");
        builder.Property(transaction => transaction.TargetAccountId).HasColumnName("target_account_id");
        builder.Property(transaction => transaction.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(transaction => transaction.TargetAmount).HasColumnName("target_amount").HasPrecision(19, 4);
        builder.Property(transaction => transaction.AdjustmentDirection).HasColumnName("adjustment_direction").HasConversion<string>().HasMaxLength(10);
        builder.Property(transaction => transaction.TransactionDate).HasColumnName("transaction_date").HasColumnType("date");
        builder.Property(transaction => transaction.CategoryId).HasColumnName("category_id");
        builder.Property(transaction => transaction.Note).HasColumnName("note").HasMaxLength(500);

        builder.HasOne(transaction => transaction.Account)
            .WithMany()
            .HasForeignKey(transaction => transaction.AccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_transactions_accounts_account_id");

        builder.HasOne(transaction => transaction.TargetAccount)
            .WithMany()
            .HasForeignKey(transaction => transaction.TargetAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_transactions_accounts_target_account_id");

        builder.HasOne(transaction => transaction.Category)
            .WithMany()
            .HasForeignKey(transaction => transaction.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_transactions_categories_category_id");

        builder.HasOne(transaction => transaction.Owner).WithMany().HasForeignKey(transaction => transaction.OwnerId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_transactions_users_owner_id");

        builder.HasIndex(transaction => transaction.TransactionDate).HasDatabaseName("ix_transactions_transaction_date");
        builder.HasIndex(transaction => new { transaction.AccountId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_account_id_transaction_date");
        builder.HasIndex(transaction => new { transaction.CategoryId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_category_id_transaction_date");
        builder.HasIndex(transaction => transaction.TargetAccountId).HasDatabaseName("ix_transactions_target_account_id");
        builder.HasIndex(transaction => new { transaction.OwnerId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_owner_id_transaction_date");
    }
}
