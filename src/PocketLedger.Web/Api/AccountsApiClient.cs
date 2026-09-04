using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class AccountsApiClient(HttpClient client) : ApiClientBase(client), IAccountService
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken token) => (await GetAsync<IReadOnlyList<AccountDto>>("api/v1/accounts", token)).Select(WebContractMapper.ToEntity).ToArray();
    public async Task<Account?> GetByIdAsync(Guid id, CancellationToken token) => (await GetOrDefaultAsync<AccountDto>($"api/v1/accounts/{id}", token))?.ToEntity();
    public async Task<Account> CreateAsync(Account account, CancellationToken token) => (await PostAsync<AccountDto, AccountDto>("api/v1/accounts", account.ToDto(), token)).ToEntity();
    public Task UpdateAsync(Account account, bool createInitialBalanceAdjustment, CancellationToken token) => PutAsync($"api/v1/accounts/{account.Id}", new AccountUpdateRequest(account.ToDto(), createInitialBalanceAdjustment), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/accounts/{id}", token);
    public Task<AccountDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken token) => GetAsync<AccountDeletionSummary>($"api/v1/accounts/{id}/deletion-summary", token);
    public Task<decimal> GetCurrentBalanceAsync(Guid accountId, CancellationToken token) => GetAsync<decimal>($"api/v1/accounts/{accountId}/balance", token);
    public Task<IReadOnlyDictionary<Guid, decimal>> GetCurrentBalancesAsync(CancellationToken token) => GetAsync<IReadOnlyDictionary<Guid, decimal>>("api/v1/accounts/balances", token);
    public Task<IReadOnlyList<AccountChoice>> GetChoicesAsync(CancellationToken token) => GetAsync<IReadOnlyList<AccountChoice>>("api/v1/accounts/choices", token);
    public async Task<IReadOnlyList<Transaction>> GetRecentTransactionsAsync(Guid accountId, int count, CancellationToken token) => (await GetAsync<IReadOnlyList<TransactionDto>>($"api/v1/accounts/{accountId}/recent-transactions?count={count}", token)).Select(WebContractMapper.ToEntity).ToArray();
}
