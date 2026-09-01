using System.Net;
using System.Text;
using PocketLedger.Web.Api;

namespace PocketLedger.Tests;

public sealed class MainBalanceWebTests
{
    [Fact]
    public async Task TransactionsApiClient_MapsCurrencyCodesAndAmounts()
    {
        using var httpClient = new HttpClient(new StaticResponseHandler("[{\"currency\":\"EUR\",\"amount\":100},{\"currency\":\"HUF\",\"amount\":100}]")) { BaseAddress = new Uri("https://api.test/") };
        var client = new TransactionsApiClient(httpClient);

        var balances = await client.CalculateMainBalanceAsync(CancellationToken.None);

        Assert.Equal("EUR", balances[0].Currency);
        Assert.Equal(100m, balances[0].Amount);
        Assert.Equal("HUF", balances[1].Currency);
        Assert.Equal(100m, balances[1].Amount);
    }

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("api/v1/transactions/main-balance", request.RequestUri?.PathAndQuery.TrimStart('/'));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
