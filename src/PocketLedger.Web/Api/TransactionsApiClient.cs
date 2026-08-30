using System.Globalization;
using PocketLedger.Models.Entities;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class TransactionsApiClient(HttpClient client) : ApiClientBase(client), ITransactionService
{
    public Task<IReadOnlyList<Transaction>> GetForMonthAsync(int year, int month, CancellationToken token) => GetAsync<IReadOnlyList<Transaction>>($"api/v1/transactions/month?year={year}&month={month}", token);
    public Task<IReadOnlyList<Transaction>> GetRecentAsync(int count, CancellationToken token) => GetAsync<IReadOnlyList<Transaction>>($"api/v1/transactions/recent?count={count}", token);
    public Task<PagedResult<Transaction>> GetFilteredAsync(TransactionFilter filter, CancellationToken token) => GetAsync<PagedResult<Transaction>>(FilterUri(filter), token);
    public Task<IReadOnlyList<TransactionDailyTotal>> GetDailyTotalsAsync(TransactionFilter filter, CancellationToken token) => PostAsync<TransactionFilter, IReadOnlyList<TransactionDailyTotal>>("api/v1/transactions/daily-totals", filter, token);
    public Task<IReadOnlyList<Transaction>> GetForExportAsync(TransactionFilter filter, CancellationToken token) => PostAsync<TransactionFilter, IReadOnlyList<Transaction>>("api/v1/transactions/export-query", filter, token);
    public Task<Transaction?> GetByIdAsync(Guid id, CancellationToken token) => GetOrDefaultAsync<Transaction>($"api/v1/transactions/{id}", token);
    public Task<Transaction> CreateAsync(Transaction transaction, CancellationToken token) => PostAsync<Transaction, Transaction>("api/v1/transactions", transaction, token);
    public Task UpdateAsync(Transaction transaction, CancellationToken token) => PutAsync($"api/v1/transactions/{transaction.Id}", transaction, token);
    public Task DeleteAsync(Guid id, CancellationToken token) => DeleteAsync($"api/v1/transactions/{id}", token);
    public Task<IReadOnlyDictionary<Guid, decimal>> CalculateAccountBalancesAsync(CancellationToken token) => GetAsync<IReadOnlyDictionary<Guid, decimal>>("api/v1/transactions/balances", token);
    public Task<decimal> CalculateMainBalanceAsync(CancellationToken token) => GetAsync<decimal>("api/v1/transactions/main-balance", token);

    private static string FilterUri(TransactionFilter filter) => Query("api/v1/transactions", new Dictionary<string, string?>
    {
        ["dateFrom"] = filter.DateFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["dateTo"] = filter.DateTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["year"] = filter.Year?.ToString(CultureInfo.InvariantCulture), ["month"] = filter.Month?.ToString(CultureInfo.InvariantCulture), ["accountId"] = filter.AccountId?.ToString(),
        ["categoryId"] = filter.CategoryId?.ToString(), ["type"] = filter.Type?.ToString(), ["amountFrom"] = filter.AmountFrom?.ToString(CultureInfo.InvariantCulture),
        ["amountTo"] = filter.AmountTo?.ToString(CultureInfo.InvariantCulture), ["search"] = filter.Search, ["page"] = filter.Page.ToString(CultureInfo.InvariantCulture), ["pageSize"] = filter.PageSize.ToString(CultureInfo.InvariantCulture)
    });
}
