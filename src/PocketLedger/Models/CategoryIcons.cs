using PocketLedger.Models.Enums;
using PocketLedger.Models.Entities;

namespace PocketLedger.Models;

public record CategoryIconDefinition(string Id, string Group, string GroupDisplayName, CategoryType CategoryType, string WebPath, string DisplayName);

public static class CategoryIcons
{
    private static readonly IReadOnlyList<(string Slug, string DisplayName)> ExpenseGroups =
    [
        ("food", "Food"),
        ("shopping", "Shopping"),
        ("fuel", "Fuel"),
        ("coffee", "Coffee"),
        ("parking", "Parking"),
        ("cinema", "Cinema"),
        ("travel", "Travel"),
        ("car-service", "Car service"),
        ("bills", "Bills"),
        ("transportation", "Transportation"),
        ("health", "Health"),
        ("gift", "Gift"),
        ("entertainment", "Entertainment"),
        ("accommodation", "Accommodation"),
        ("streaming", "Streaming"),
        ("phone", "Phone"),
        ("internet", "Internet"),
        ("bank", "Bank"),
        ("insurance", "Insurance"),
        ("other-expense", "Other expense")
    ];

    private static readonly IReadOnlyList<(string Slug, string DisplayName)> IncomeGroups =
    [
        ("salary", "Salary"),
        ("investment", "Investment"),
        ("cashback", "Cashback"),
        ("interest", "Interest"),
        ("other-income", "Other income")
    ];

    public static readonly IReadOnlyList<CategoryIconDefinition> All =
    [
        .. Create(CategoryType.Expense, ExpenseGroups),
        .. Create(CategoryType.Income, IncomeGroups)
    ];

    public static IReadOnlyList<CategoryIconDefinition> For(CategoryType categoryType) => All.Where(icon => icon.CategoryType == categoryType).ToList();

    public static CategoryIconDefinition DefaultFor(CategoryType categoryType)
    {
        var defaultGroup = categoryType == CategoryType.Income ? "other-income" : "other-expense";
        return All.First(icon => icon.Group == defaultGroup);
    }

    public static CategoryIconDefinition Resolve(string? id, CategoryType categoryType)
    {
        return All.FirstOrDefault(icon => icon.CategoryType == categoryType && string.Equals(icon.Id, id, StringComparison.Ordinal))
            ?? DefaultFor(categoryType);
    }

    public static bool IsCompatible(string? id, CategoryType categoryType)
    {
        return All.Any(icon => icon.CategoryType == categoryType && string.Equals(icon.Id, id, StringComparison.Ordinal));
    }

    public static CategoryIconDefinition Resolve(Category category)
    {
        var icon = category.ParentCategory?.Icon ?? category.Icon;
        return Resolve(icon, category.Type);
    }

    private static IEnumerable<CategoryIconDefinition> Create(CategoryType categoryType, IEnumerable<(string Slug, string DisplayName)> groups)
    {
        return groups.SelectMany(group => Enumerable.Range(1, 5).Select(index => new CategoryIconDefinition(
            $"{group.Slug}-{index}",
            group.Slug,
            group.DisplayName,
            categoryType,
            $"/images/category-icons/{group.Slug}/{group.Slug}-{index}.png",
            $"{group.DisplayName} icon {index}")));
    }
}
