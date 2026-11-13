using Microsoft.EntityFrameworkCore;
using PocketLedger.Data;
using PocketLedger.Models;
using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Services;

public class CategoryService(PocketLedgerDbContext dbContext) : ICategoryService
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories.AsNoTracking().Include(category => category.Subcategories).Where(category => category.ParentCategoryId == null).ToListAsync(cancellationToken);
        foreach (var category in categories)
            category.Subcategories = category.Subcategories.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Name).ToList();
        return categories.OrderBy(category => category.Type).ThenBy(category => category.DisplayOrder).ThenBy(category => category.Name).ToList();
    }

    public Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Categories.AsNoTracking().Include(category => category.ParentCategory).SingleOrDefaultAsync(category => category.Id == id, cancellationToken);
    }

    public async Task<Category> CreateAsync(Category category, CancellationToken cancellationToken)
    {
        var parent = await LoadParentAsync(category.ParentCategoryId, cancellationToken);
        PrepareAndValidate(category, parent);
        category.Id = category.Id == Guid.Empty ? Guid.NewGuid() : category.Id;
        category.ParentCategory = null;
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task UpdateAsync(Category category, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Categories.SingleOrDefaultAsync(item => item.Id == category.Id, cancellationToken)
            ?? throw new EntityNotFoundException("Category not found.");
        var parent = await LoadParentAsync(category.ParentCategoryId, cancellationToken);
        PrepareAndValidate(category, parent);

        var hasSubcategories = await dbContext.Categories.AnyAsync(item => item.ParentCategoryId == category.Id, cancellationToken);
        if (category.ParentCategoryId is not null && hasSubcategories)
        {
            throw new BusinessRuleException("A category containing subcategories cannot become a subcategory.");
        }

        if (existing.Type != category.Type && hasSubcategories)
        {
            throw new BusinessRuleException("A main category containing subcategories cannot change type.");
        }

        if (!string.Equals(category.Name, DefaultCategories.DisplayName(existing), StringComparison.Ordinal))
        {
            existing.Name = category.Name;
            existing.StarterCategoryKey = null;
        }
        existing.Type = category.Type;
        existing.Icon = category.Icon;
        existing.ParentCategoryId = category.ParentCategoryId;
        existing.DisplayOrder = category.DisplayOrder;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Category not found.");
        var hasTransactions = await dbContext.Transactions.AnyAsync(transaction => transaction.CategoryId == id, cancellationToken);
        var hasRecurringTransactions = await dbContext.RecurringTransactions.AnyAsync(template => template.CategoryId == id, cancellationToken);
        var hasSubcategories = await dbContext.Categories.AnyAsync(item => item.ParentCategoryId == id, cancellationToken);
        var hasPlannerItems = await dbContext.PlannerItems.AnyAsync(item => item.CategoryId == id, cancellationToken);
        CategoryRules.EnsureCanDelete(hasTransactions || hasRecurringTransactions || hasPlannerItems, hasSubcategories);
        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryChoice>> GetChoicesAsync(CategoryType? type, Guid? excludeId, CancellationToken cancellationToken)
    {
        var query = dbContext.Categories.AsNoTracking().AsQueryable();
        if (type is not null)
        {
            query = query.Where(category => category.Type == type);
        }

        if (excludeId is not null)
        {
            query = query.Where(category => category.Id != excludeId);
        }

        var categories = await query.Include(category => category.ParentCategory).ToListAsync(cancellationToken);
        var choices = categories.OrderBy(category => category.Type).ThenBy(category => category.ParentCategoryId).ThenBy(category => category.DisplayOrder).ThenBy(category => category.Name)
            .Select(category => new CategoryChoice(category.Id, DefaultCategories.DisplayName(category), category.Type, category.Icon, category.ParentCategoryId, category.ParentCategory is { } parent ? DefaultCategories.DisplayName(parent) : null, category.ParentCategory?.Icon)).ToList();

        var ordered = new List<CategoryChoice>();
        foreach (var parent in choices.Where(choice => !choice.IsSubcategory))
        {
            ordered.Add(parent);
            ordered.AddRange(choices.Where(choice => choice.ParentCategoryId == parent.Id));
        }

        return ordered;
    }

    public async Task<IReadOnlyList<CategoryChoice>> GetStarterCandidatesAsync(CancellationToken cancellationToken)
    {
        var candidates = await dbContext.Categories.AsNoTracking().Include(category => category.ParentCategory)
            .Where(category => category.StarterCategoryKey == null).ToListAsync(cancellationToken);
        return candidates.Where(IsStarterCandidate).OrderBy(category => category.ParentCategory?.Name ?? category.Name)
            .ThenBy(category => category.ParentCategoryId is not null).ThenBy(category => category.Name)
            .Select(category => new CategoryChoice(category.Id, category.Name, category.Type, category.Icon, category.ParentCategoryId, category.ParentCategory?.Name, category.ParentCategory?.Icon))
            .ToArray();
    }

    public async Task EnableStarterTranslationsAsync(IReadOnlyList<Guid> categoryIds, CancellationToken cancellationToken)
    {
        var ids = categoryIds.Distinct().ToArray();
        var categories = await dbContext.Categories.Include(category => category.ParentCategory)
            .Where(category => ids.Contains(category.Id)).ToListAsync(cancellationToken);
        if (categories.Count != ids.Length || categories.Any(category => !IsStarterCandidate(category)))
            throw new BusinessRuleException("One or more selected categories no longer match a starter category.");

        foreach (var category in categories)
            category.StarterCategoryKey = category.Name;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsStarterCandidate(Category category) => DefaultCategories.All.Any(definition =>
        definition.Type == category.Type && (category.ParentCategory is { } parent
            ? parent.ParentCategoryId is null && parent.Type == definition.Type && parent.Name == definition.Name && definition.Subcategories.Contains(category.Name)
            : category.ParentCategoryId is null && category.Name == definition.Name));

    private async Task<Category?> LoadParentAsync(Guid? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        return await dbContext.Categories.AsNoTracking().SingleOrDefaultAsync(category => category.Id == parentId, cancellationToken)
            ?? throw new BusinessRuleException("The selected parent category does not exist.");
    }

    private static void PrepareAndValidate(Category category, Category? parent)
    {
        CategoryRules.Validate(category.Name, category.Type, category.DisplayOrder, parent);
        CategoryRules.ValidateParent(category, parent);
        CategoryRules.ValidateIcon(category.Icon, category.ParentCategoryId is not null);
        category.Name = category.Name.Trim();
        category.Icon = category.ParentCategoryId is null ? CategoryRules.ValidateMainCategoryIcon(category.Icon, category.Type) : null;
    }
}
