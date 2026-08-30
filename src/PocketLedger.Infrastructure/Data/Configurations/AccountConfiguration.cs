using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        builder.HasKey(account => account.Id).HasName("pk_accounts");
        builder.Property(account => account.Id).HasColumnName("id");
        builder.Property(account => account.OwnerId).HasColumnName("owner_id");
        builder.Property(account => account.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(account => account.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(account => account.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(account => account.InitialBalance).HasColumnName("initial_balance").HasPrecision(19, 4);
        builder.Property(account => account.Icon).HasColumnName("icon").HasMaxLength(100);
        builder.Property(account => account.Color).HasColumnName("color").HasMaxLength(7).IsRequired();
        builder.Property(account => account.DisplayOrder).HasColumnName("display_order");
        builder.Property(account => account.IncludeInMainBalance).HasColumnName("include_in_main_balance");
        builder.Property(account => account.IncludeInNetWorth).HasColumnName("include_in_net_worth");
        builder.Property(account => account.IncludeInStatistics).HasColumnName("include_in_statistics");

        builder.HasIndex(account => new { account.OwnerId, account.DisplayOrder }).HasDatabaseName("ix_accounts_owner_id_display_order");
    }
}
