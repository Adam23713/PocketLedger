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
            tableBuilder.HasCheckConstraint("ck_transactions_transfer_target", "(type = 'Transfer' AND account_id IS NOT NULL AND target_account_id IS NOT NULL) OR (type <> 'Transfer' AND target_account_id IS NULL)");
            tableBuilder.HasCheckConstraint("ck_transactions_target_amount_transfer", "target_amount IS NULL OR type = 'Transfer'");
            tableBuilder.HasCheckConstraint("ck_transactions_exchange_rate", "(type = 'Transfer' AND exchange_rate > 0 AND target_amount > 0 AND target_currency IS NOT NULL) OR (type <> 'Transfer' AND exchange_rate IS NULL AND target_amount IS NULL AND target_currency IS NULL)");
            tableBuilder.HasCheckConstraint("ck_transactions_adjustment_direction", "(type = 'Adjustment' AND adjustment_direction IS NOT NULL) OR (type <> 'Adjustment' AND adjustment_direction IS NULL)");
            tableBuilder.HasCheckConstraint("ck_transactions_account", "(type = 'DebtEntry' AND account_id IS NULL) OR (type <> 'DebtEntry' AND account_id IS NOT NULL)");
            tableBuilder.HasCheckConstraint("ck_transactions_debt_operation", "(debt_id IS NULL AND debt_operation_type IS NULL) OR (debt_id IS NOT NULL AND debt_operation_type IS NOT NULL)");
        });

        builder.HasKey(transaction => transaction.Id).HasName("pk_transactions");
        builder.Property(transaction => transaction.Id).HasColumnName("id");
        builder.Property(transaction => transaction.OwnerId).HasColumnName("owner_id");
        builder.Property(transaction => transaction.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(transaction => transaction.AccountId).HasColumnName("account_id");
        builder.Property(transaction => transaction.TargetAccountId).HasColumnName("target_account_id");
        builder.Property(transaction => transaction.Amount).HasColumnName("amount").HasPrecision(19, 4);
        builder.Property(transaction => transaction.TargetAmount).HasColumnName("target_amount").HasPrecision(19, 4);
        builder.Property(transaction => transaction.ExchangeRate).HasColumnName("exchange_rate").HasPrecision(19, 8);
        builder.Property(transaction => transaction.SourceCurrency).HasColumnName("source_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(transaction => transaction.TargetCurrency).HasColumnName("target_currency").HasMaxLength(3).IsFixedLength();
        builder.Property(transaction => transaction.AdjustmentDirection).HasColumnName("adjustment_direction").HasConversion<string>().HasMaxLength(10);
        builder.Property(transaction => transaction.TransactionDate).HasColumnName("transaction_date").HasColumnType("date");
        builder.Property(transaction => transaction.TransactionTime).HasColumnName("transaction_time").HasColumnType("time without time zone");
        builder.Property(transaction => transaction.OccurredAtUtc).HasColumnName("occurred_at_utc").HasColumnType("timestamp with time zone");
        builder.Property(transaction => transaction.CategoryId).HasColumnName("category_id");
        builder.Property(transaction => transaction.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(transaction => transaction.DebtId).HasColumnName("debt_id");
        builder.Property(transaction => transaction.DebtOperationType).HasColumnName("debt_operation_type").HasConversion<string>().HasMaxLength(40);

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

        builder.HasOne(transaction => transaction.Debt).WithMany(debt => debt.Transactions).HasForeignKey(transaction => transaction.DebtId).OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_transactions_debts_debt_id");

        builder.HasIndex(transaction => transaction.TransactionDate).HasDatabaseName("ix_transactions_transaction_date");
        builder.HasIndex(transaction => new { transaction.AccountId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_account_id_transaction_date");
        builder.HasIndex(transaction => new { transaction.CategoryId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_category_id_transaction_date");
        builder.HasIndex(transaction => transaction.TargetAccountId).HasDatabaseName("ix_transactions_target_account_id");
        builder.HasIndex(transaction => new { transaction.OwnerId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_owner_id_transaction_date");
        builder.HasIndex(transaction => new { transaction.DebtId, transaction.TransactionDate }).HasDatabaseName("ix_transactions_debt_id_transaction_date");
    }
}
