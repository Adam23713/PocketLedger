using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class GoalsApiClient(HttpClient client) : ApiClientBase(client), IGoalService
{
    public async Task<IReadOnlyList<Goal>> GetAllAsync(CancellationToken token) => (await GetAsync<IReadOnlyList<GoalDto>>("api/v1/goals", token)).Select(WebContractMapper.ToEntity).ToArray();
    public async Task<Goal?> GetByIdAsync(Guid id, CancellationToken token) => (await GetOrDefaultAsync<GoalDto>($"api/v1/goals/{id}", token))?.ToEntity();
    public async Task<Goal> CreateAsync(Goal goal, CancellationToken token) => (await PostAsync<GoalDto, GoalDto>("api/v1/goals", goal.ToDto(), token)).ToEntity();
    public Task UpdateAsync(Goal goal, CancellationToken token) => PutAsync($"api/v1/goals/{goal.Id}", goal.ToDto(), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/goals/{id}", token);
    public async Task<Goal> AddContributionAsync(Guid id, decimal amount, CancellationToken token) => (await PostAsync<GoalContributionRequest, GoalDto>($"api/v1/goals/{id}/contributions", new GoalContributionRequest(amount), token)).ToEntity();
}
