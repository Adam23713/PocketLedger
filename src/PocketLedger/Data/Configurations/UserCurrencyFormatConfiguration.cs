using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public sealed class UserCurrencyFormatConfiguration : IEntityTypeConfiguration<UserCurrencyFormat>
{
    public void Configure(EntityTypeBuilder<UserCurrencyFormat> builder)
    {
        builder.ToTable("user_currency_formats", table =>
        {
            table.HasCheckConstraint("ck_user_currency_formats_decimal_places", "decimal_places BETWEEN 0 AND 4");
            table.HasCheckConstraint("ck_user_currency_formats_separators", "decimal_separator <> thousands_separator");
        });
        builder.HasKey(item => new { item.UserId, item.CurrencyCode });
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsFixedLength();
        builder.Property(item => item.DecimalPlaces).HasColumnName("decimal_places");
        builder.Property(item => item.DecimalSeparator).HasColumnName("decimal_separator").HasMaxLength(1);
        builder.Property(item => item.ThousandsSeparator).HasColumnName("thousands_separator").HasMaxLength(1);
        builder.Property(item => item.CurrencyDisplay).HasColumnName("currency_display").HasConversion<string>().HasMaxLength(10);
        builder.Property(item => item.CurrencyPosition).HasColumnName("currency_position").HasConversion<string>().HasMaxLength(10);
        builder.Property(item => item.UseSpace).HasColumnName("use_space");
        builder.HasOne(item => item.User).WithMany(user => user.CurrencyFormats).HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
