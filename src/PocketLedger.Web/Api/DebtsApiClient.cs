using System.Globalization;
using PocketLedger.Contracts;
using PocketLedger.Models.Entities;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class DebtsApiClient(HttpClient client) : ApiClientBase(client), IDebtService
{
    public Task<IReadOnlyList<DebtSummary>> GetAllAsync(CancellationToken token) => GetAsync<IReadOnlyList<DebtSummary>>("api/v1/debts", token);
    public Task<DebtDetails?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<DebtDetails>($"api/v1/debts/{id}", token);
    public Task<Transaction?> GetOperationAsync(Guid transactionId, CancellationToken token) => GetOrDefaultAsync<Transaction>($"api/v1/debts/operations/{transactionId}", token);
    public Task<Debt> CreateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken token) => PostAsync<DebtWriteRequest, Debt>("api/v1/debts", new DebtWriteRequest(debt, recurringPayment), token);
    public Task UpdateAsync(Debt debt, RecurringPaymentInput? recurringPayment, CancellationToken token) => PutAsync($"api/v1/debts/{debt.Id}", new DebtWriteRequest(debt, recurringPayment), token);
    public Task<DebtDeletionSummary> GetDeletionSummaryAsync(Guid id, CancellationToken token) => GetAsync<DebtDeletionSummary>($"api/v1/debts/{id}/deletion-summary", token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/debts/{id}", token);
    public Task<Transaction> AddOperationAsync(Guid debtId, DebtOperationInput input, CancellationToken token) => PostAsync<DebtOperationWriteRequest, Transaction>($"api/v1/debts/{debtId}/operations", new DebtOperationWriteRequest(input), token);
    public Task<Transaction> UpdateOperationAsync(Guid transactionId, DebtOperationInput input, CancellationToken token) => PutAndReadAsync<Transaction>($"api/v1/debts/operations/{transactionId}", new DebtOperationWriteRequest(input), token);
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
