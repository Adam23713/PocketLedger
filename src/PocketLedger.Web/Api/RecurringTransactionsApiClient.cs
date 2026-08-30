using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class RecurringTransactionsApiClient(HttpClient client) : ApiClientBase(client), IRecurringTransactionService
{
    public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken token) => GetAsync<IReadOnlyList<RecurringTransaction>>("api/v1/recurring-transactions", token);
    public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<RecurringTransaction>($"api/v1/recurring-transactions/{id}", token);
    public Task<RecurringTransaction> CreateAsync(RecurringTransaction template, CancellationToken token) => PostAsync<RecurringTransaction, RecurringTransaction>("api/v1/recurring-transactions", template, token);
    public Task UpdateAsync(RecurringTransaction template, CancellationToken token) => PutAsync($"api/v1/recurring-transactions/{template.Id}", template, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/recurring-transactions/{id}", token);
}
