using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class RecurringTransactionOccurrenceConfiguration : IEntityTypeConfiguration<RecurringTransactionOccurrence>
{
    public void Configure(EntityTypeBuilder<RecurringTransactionOccurrence> builder)
    {
        builder.ToTable("recurring_transaction_occurrences");
        builder.HasKey(occurrence => occurrence.Id).HasName("pk_recurring_transaction_occurrences");
        builder.Property(occurrence => occurrence.Id).HasColumnName("id");
        builder.Property(occurrence => occurrence.OwnerId).HasColumnName("owner_id");
        builder.Property(occurrence => occurrence.RecurringTransactionId).HasColumnName("recurring_transaction_id");
        builder.Property(occurrence => occurrence.OccurrenceDate).HasColumnName("occurrence_date").HasColumnType("date");
        builder.Property(occurrence => occurrence.TransactionId).HasColumnName("transaction_id");

        builder.HasOne(occurrence => occurrence.RecurringTransaction).WithMany(template => template.Occurrences).HasForeignKey(occurrence => occurrence.RecurringTransactionId).OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_recurring_transaction_occurrences_recurring_transaction_id");
        builder.HasOne(occurrence => occurrence.Transaction).WithMany().HasForeignKey(occurrence => occurrence.TransactionId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_recurring_transaction_occurrences_transactions_transaction_id");
        builder.HasIndex(occurrence => new { occurrence.RecurringTransactionId, occurrence.OccurrenceDate }).IsUnique().HasDatabaseName("ux_recurring_transaction_occurrences_template_date");
        builder.HasIndex(occurrence => occurrence.OwnerId).HasDatabaseName("ix_recurring_transaction_occurrences_owner_id");
        builder.HasIndex(occurrence => occurrence.TransactionId).IsUnique().HasDatabaseName("ux_recurring_transaction_occurrences_transaction_id");
    }
}
