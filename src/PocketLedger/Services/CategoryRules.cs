using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Models;

namespace PocketLedger.Services;

public static class CategoryRules
{
    public static void Validate(string? name, CategoryType type, int displayOrder, Category? parent)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new BusinessRuleException("Category name is required.");
        }

        if (displayOrder < 0)
        {
            throw new BusinessRuleException("Display order cannot be negative.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new BusinessRuleException("The selected category type is invalid.");
        }

        if (parent?.ParentCategoryId is not null)
        {
            throw new BusinessRuleException("A subcategory cannot contain another subcategory.");
        }
    }

    public static void ValidateParent(Category child, Category? parent)
    {
        if (parent is null)
        {
            return;
        }

        if (parent.Id == child.Id)
        {
            throw new BusinessRuleException("A category cannot be its own parent.");
        }

        if (parent.Type != child.Type)
        {
            throw new BusinessRuleException("Parent and subcategory must have the same category type.");
        }

        if (!string.IsNullOrWhiteSpace(child.Icon))
        {
            throw new BusinessRuleException("Subcategories cannot use an icon.");
        }
    }

    public static void ValidateIcon(string? icon, bool isSubcategory)
    {
        if (isSubcategory && !string.IsNullOrWhiteSpace(icon))
        {
            throw new BusinessRuleException("Subcategories cannot use an icon.");
        }

        if (!isSubcategory && !string.IsNullOrWhiteSpace(icon) && !CategoryIcons.All.Any(candidate => string.Equals(candidate.Id, icon, StringComparison.Ordinal)))
        {
            throw new BusinessRuleException("The selected category icon is invalid.");
        }
    }

    public static string ValidateMainCategoryIcon(string? icon, CategoryType categoryType)
    {
        if (!CategoryIcons.IsCompatible(icon, categoryType))
        {
            throw new BusinessRuleException("The selected icon is not valid for this category type.");
        }

        return icon!;
    }

    public static void EnsureCanDelete(bool hasTransactions, bool hasSubcategories)
    {
        if (hasTransactions)
        {
            throw new BusinessRuleException("The category cannot be deleted because transactions reference it.");
        }

        if (hasSubcategories)
        {
            throw new BusinessRuleException("The category cannot be deleted while it contains subcategories.");
        }
    }
}
