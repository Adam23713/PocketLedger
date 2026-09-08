using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class PlannerApiClient(HttpClient client) : ApiClientBase(client), IPlannerService
{
    public Task<PlannerMonth> GetMonthAsync(int year, int month, CancellationToken token) => GetAsync<PlannerMonth>($"api/v1/planner/{year}/{month}", token);
    public Task UpdateOpeningBalanceAsync(int year, int month, Guid accountId, PlannerOpeningBalanceInput input, CancellationToken token) => PutAsync($"api/v1/planner/{year}/{month}/accounts/{accountId}", input, token);
    public Task<PlannerItemInput?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<PlannerItemInput>($"api/v1/planner/items/{id}", token);
    public Task<Guid> CreateAsync(PlannerItemInput input, CancellationToken token) => PostAsync<PlannerItemInput, Guid>("api/v1/planner/items", input, token);
    public Task UpdateAsync(Guid id, PlannerItemInput input, CancellationToken token) => PutAsync($"api/v1/planner/items/{id}", input, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/planner/items/{id}", token);
}
