using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;

namespace PocketLedger.Services.Interfaces;

public record CategoryChoice(Guid Id, string Name, CategoryType Type, string? Icon, Guid? ParentCategoryId, string? ParentName, string? ParentIcon)
{
    public bool IsSubcategory => ParentCategoryId is not null;
    public string? EffectiveIcon => ParentIcon ?? Icon;
}

public interface ICategoryService
{
    Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken cancellationToken);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<Category> CreateAsync(Category category, CancellationToken cancellationToken);
    Task UpdateAsync(Category category, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CategoryChoice>> GetChoicesAsync(CategoryType? type, Guid? excludeId, CancellationToken cancellationToken);
}
