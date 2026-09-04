using PocketLedger.Models.Entities;
using PocketLedger.Models.Enums;
using PocketLedger.Services.Interfaces;
using PocketLedger.Contracts;

namespace PocketLedger.Web.Api;

public sealed class CategoriesApiClient(HttpClient client) : ApiClientBase(client), ICategoryService
{
    public async Task<IReadOnlyList<Category>> GetAllAsync(CancellationToken token) => (await GetAsync<IReadOnlyList<CategoryDto>>("api/v1/categories", token)).Select(WebContractMapper.ToEntity).ToArray();
    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken token) => (await GetOrDefaultAsync<CategoryDto>($"api/v1/categories/{id}", token))?.ToEntity();
    public async Task<Category> CreateAsync(Category category, CancellationToken token) => (await PostAsync<CategoryDto, CategoryDto>("api/v1/categories", category.ToDto(), token)).ToEntity();
    public Task UpdateAsync(Category category, CancellationToken token) => PutAsync($"api/v1/categories/{category.Id}", category.ToDto(), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/categories/{id}", token);
    public Task<IReadOnlyList<CategoryChoice>> GetChoicesAsync(CategoryType? type, Guid? excludeId, CancellationToken token) => GetAsync<IReadOnlyList<CategoryChoice>>(Query("api/v1/categories/choices", new Dictionary<string, string?> { ["type"] = type?.ToString(), ["excludeId"] = excludeId?.ToString() }), token);
}
