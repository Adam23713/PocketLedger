using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class AccountsApiClient(HttpClient client) : ApiClientBase(client), IAccountService
{
    public Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken token) => GetAsync<IReadOnlyList<Account>>("api/v1/accounts", token);
    public Task<Account?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<Account>($"api/v1/accounts/{id}", token);
    public Task<Account> CreateAsync(Account account, CancellationToken token) => PostAsync<Account, Account>("api/v1/accounts", account, token);
    public Task UpdateAsync(Account account, bool createInitialBalanceAdjustment, CancellationToken token) => PutAsync($"api/v1/accounts/{account.Id}", new AccountUpdateRequest(account, createInitialBalanceAdjustment), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/accounts/{id}", token);
    public Task<AccountDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken token) => GetAsync<AccountDeletionSummary>($"api/v1/accounts/{id}/deletion-summary", token);
    public Task<decimal> GetCurrentBalanceAsync(Guid accountId, CancellationToken token) => GetAsync<decimal>($"api/v1/accounts/{accountId}/balance", token);
    public Task<IReadOnlyDictionary<Guid, decimal>> GetCurrentBalancesAsync(CancellationToken token) => GetAsync<IReadOnlyDictionary<Guid, decimal>>("api/v1/accounts/balances", token);
    public Task<IReadOnlyList<AccountChoice>> GetChoicesAsync(CancellationToken token) => GetAsync<IReadOnlyList<AccountChoice>>("api/v1/accounts/choices", token);
    public Task<IReadOnlyList<Transaction>> GetRecentTransactionsAsync(Guid accountId, int count, CancellationToken token) => GetAsync<IReadOnlyList<Transaction>>($"api/v1/accounts/{accountId}/recent-transactions?count={count}", token);
}
