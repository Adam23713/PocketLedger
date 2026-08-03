using System.ComponentModel.DataAnnotations;
using PocketLedger.Models;
using PocketLedger.Models.Enums;

namespace PocketLedger.Models.ViewModels.Categories;

public class CategoryListViewModel
{
    public IReadOnlyList<CategoryListItemViewModel> IncomeCategories { get; init; } = [];
    public IReadOnlyList<CategoryListItemViewModel> ExpenseCategories { get; init; } = [];
}

public class CategoryListItemViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CategoryType Type { get; init; }
    public string? Icon { get; init; }
    public string? IconPath { get; init; }
    public string? IconAlt { get; init; }
    public int DisplayOrder { get; init; }
    public IReadOnlyList<CategoryListItemViewModel> Subcategories { get; init; } = [];
}

public class CategoryFormViewModel
{
    public Guid Id { get; set; }

    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public CategoryType Type { get; set; }

    [StringLength(100)]
    public string? Icon { get; set; }

    public IReadOnlyList<CategoryIconDefinition> AvailableIcons { get; init; } = CategoryIcons.All;

    public Guid? ParentCategoryId { get; set; }

    [Range(0, int.MaxValue)]
    public int DisplayOrder { get; set; }

    public IReadOnlyList<CategoryParentOptionViewModel> ParentCategories { get; set; } = [];
}

public class CategoryParentOptionViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CategoryType Type { get; init; }
}

public class CategoryDeleteViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public CategoryType Type { get; init; }
    public string? ParentName { get; init; }
}
