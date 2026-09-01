using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;
using PocketLedger.Contracts;

namespace PocketLedger.Web.Api;

public sealed class RecurringTransactionsApiClient(HttpClient client) : ApiClientBase(client), IRecurringTransactionService
{
    public async Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken token) => (await GetAsync<IReadOnlyList<RecurringTransactionDto>>("api/v1/recurring-transactions", token)).Select(WebContractMapper.ToEntity).ToArray();
    public async Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken token) => (await GetOrDefaultAsync<RecurringTransactionDto>($"api/v1/recurring-transactions/{id}", token))?.ToEntity();
    public async Task<RecurringTransaction> CreateAsync(RecurringTransaction template, CancellationToken token) => (await PostAsync<RecurringTransactionDto, RecurringTransactionDto>("api/v1/recurring-transactions", template.ToDto(), token)).ToEntity();
    public Task UpdateAsync(RecurringTransaction template, CancellationToken token) => PutAsync($"api/v1/recurring-transactions/{template.Id}", template.ToDto(), token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/recurring-transactions/{id}", token);
}
