using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PocketLedger.Models.Entities;

namespace PocketLedger.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint("ck_categories_not_self_referencing", "parent_category_id IS NULL OR parent_category_id <> id");
            tableBuilder.HasCheckConstraint("ck_categories_subcategory_icon", "parent_category_id IS NULL OR icon IS NULL");
        });

        builder.HasKey(category => category.Id).HasName("pk_categories");
        builder.Property(category => category.Id).HasColumnName("id");
        builder.Property(category => category.OwnerId).HasColumnName("owner_id").IsConcurrencyToken();
        builder.Property(category => category.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(category => category.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(category => category.Icon).HasColumnName("icon").HasMaxLength(100);
        builder.Property(category => category.ParentCategoryId).HasColumnName("parent_category_id");
        builder.Property(category => category.DisplayOrder).HasColumnName("display_order");

        builder.HasOne(category => category.ParentCategory)
            .WithMany(category => category.Subcategories)
            .HasForeignKey(category => category.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_categories_categories_parent_category_id");

        builder.HasIndex(category => new { category.Type, category.DisplayOrder }).HasDatabaseName("ix_categories_type_display_order");
        builder.HasIndex(category => category.ParentCategoryId).HasDatabaseName("ix_categories_parent_category_id");
        builder.HasIndex(category => new { category.OwnerId, category.Type, category.DisplayOrder }).HasDatabaseName("ix_categories_owner_id_type_display_order");
    }
}
