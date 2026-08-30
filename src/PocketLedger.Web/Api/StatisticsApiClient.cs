using PocketLedger.Services.Interfaces;

namespace PocketLedger.Web.Api;

public sealed class StatisticsApiClient(HttpClient client) : ApiClientBase(client), IStatisticsService
{
    public Task<IReadOnlyList<string>> GetAvailableCurrenciesAsync(int year, int month, CancellationToken token) => GetAsync<IReadOnlyList<string>>($"api/v1/statistics/currencies?year={year}&month={month}", token);
    public Task<StatisticsSummary> GetSummaryAsync(int year, int month, string currency, CancellationToken token) => GetAsync<StatisticsSummary>($"api/v1/statistics/summary?year={year}&month={month}&currency={Uri.EscapeDataString(currency)}", token);
}
