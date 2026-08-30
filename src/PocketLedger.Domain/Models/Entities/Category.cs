using PocketLedger.Models.Enums;

namespace PocketLedger.Models.Entities;

public class Category
{
    public Guid OwnerId { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CategoryType Type { get; set; }
    public string? Icon { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> Subcategories { get; set; } = [];
    public int DisplayOrder { get; set; }
}
