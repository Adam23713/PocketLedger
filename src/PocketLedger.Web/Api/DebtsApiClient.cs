using System.Globalization;
using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class DebtsApiClient(HttpClient client) : ApiClientBase(client), IDebtService
{
    public async Task<IReadOnlyList<DebtSummary>> GetAllAsync(CancellationToken token) => (await GetAsync<IReadOnlyList<DebtSummaryResponse>>("api/v1/debts", token)).Select(WebContractMapper.ToServiceModel).ToArray();
    public async Task<DebtDetails?> GetByIdAsync(Guid id, CancellationToken token) => (await GetOrDefaultAsync<DebtDetailsResponse>($"api/v1/debts/{id}", token))?.ToServiceModel();
    public async Task<Transaction?> GetOperationAsync(Guid transactionId, CancellationToken token) => (await GetOrDefaultAsync<TransactionDto>($"api/v1/debts/operations/{transactionId}", token))?.ToEntity();
    public async Task<Debt> CreateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken token) => (await PostAsync<DebtWriteRequest, DebtDto>("api/v1/debts", new DebtWriteRequest(debt.ToDto(), recurringPayment), token)).ToEntity();
    public Task UpdateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken token) => PutAsync($"api/v1/debts/{debt.Id}", new DebtWriteRequest(debt.ToDto(), recurringPayment), token);
    public Task<DebtDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken token) => GetAsync<DebtDeletionSummary>($"api/v1/debts/{id}/deletion-summary", token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/debts/{id}", token);
    public async Task<Transaction> AddOperationAsync(Guid debtId, DebtOperationInput input, CancellationToken token) => (await PostAsync<DebtOperationWriteRequest, TransactionDto>($"api/v1/debts/{debtId}/operations", new DebtOperationWriteRequest(input), token)).ToEntity();
    public async Task<Transaction> UpdateOperationAsync(Guid transactionId, DebtOperationInput input, CancellationToken token) => (await PutAndReadAsync<TransactionDto>($"api/v1/debts/operations/{transactionId}", new DebtOperationWriteRequest(input), token)).ToEntity();
    public Task DeleteOperationAsync(Guid transactionId, CancellationToken token) => DeleteAsync($"api/v1/debts/operations/{transactionId}", token);
    public Task CloseAsync(Guid id, CancellationToken token) => PostAsync($"api/v1/debts/{id}/close", new { }, token);
    public Task ReopenAsync(Guid id, CancellationToken token) => PostAsync($"api/v1/debts/{id}/reopen", new { }, token);
    public Task<IReadOnlyList<DebtFundingWarning>> GetFundingWarningsAsync(DateOnly today, CancellationToken token) => GetAsync<IReadOnlyList<DebtFundingWarning>>($"api/v1/debts/funding-warnings?today={today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}", token);

    private async Task<T> PutAndReadAsync<T>(string uri, object request, CancellationToken token)
    {
        using var response = await HttpClient.PutAsJsonAsync(uri, request, JsonOptions, token);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, token))!;
    }
}
