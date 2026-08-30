using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class CategoriesApiClient(HttpClient client) : ApiClientBase(client), ICategoryService
{
    public Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token) => GetAsync<IReadOnlyList<Category>>("api/v1/categories", token);
    public Task<Category?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<Category>($"api/v1/categories/{id}", token);
    public Task<Category> CreateAsync(Category category, CancellationToken token) => PostAsync<Category, Category>("api/v1/categories", category, token);
    public Task UpdateAsync(Category category, CancellationToken token) => PutAsync($"api/v1/categories/{category.Id}", category, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/categories/{id}", token);
    public Task<IReadOnlyList<CategoryChoice>> GetChoicesAsync(CategoryType? type, Guid? excludeId, CancellationToken token) => GetAsync<IReadOnlyList<CategoryChoice>>(Query("api/v1/categories/choices", new Dictionary<string, string?> { ["type"] = type?.ToString(), ["excludeId"] = excludeId?.ToString() }), token);
}
