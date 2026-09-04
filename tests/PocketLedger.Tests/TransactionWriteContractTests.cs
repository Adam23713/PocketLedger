using System.Net;
using System.Text;
using System.Text.Json;
using PocketLedger.Contracts;
using PocketLedger.Models.Enums;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;
using PocketLedger.Web.Api;

namespace PocketLedger.Tests;

public sealed class TransactionWriteContractTests
{
    [Fact]
    public void ApplicationWriteInterface_DoesNotAcceptMutableTransactionEntities()
    {
        var writeMethods = typeof(ITransactionService).GetMethods().Where(method => method.Name is nameof(ITransactionService.CreateAsync) or nameof(ITransactionService.UpdateAsync));

        Assert.All(writeMethods.SelectMany(method => method.GetParameters()), parameter => Assert.NotEqual(typeof(PocketLedger.Models.Entities.Transaction), parameter.ParameterType));
    }

    [Fact]
    public async Task TransactionsApiClient_UsesDedicatedCreateAndUpdateContracts()
    {
        var id = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var handler = new CapturingHandler(id, accountId);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        var client = new TransactionsApiClient(httpClient);
        var date = new DateOnly(2026, 9, 2);
        var time = new TimeOnly(10, 30);

        await client.CreateAsync(new TransactionCreateInput(TransactionType.Adjustment, accountId, null, 10m, null, null, AdjustmentDirection.Increase, date, time, null, "create"), CancellationToken.None);
        await client.UpdateAsync(id, new TransactionUpdateInput(TransactionType.Adjustment, accountId, null, 20m, null, null, AdjustmentDirection.Decrease, date, time, null, "update"), CancellationToken.None);

        Assert.Equal(["POST api/v1/transactions", $"PUT api/v1/transactions/{id}"], handler.Requests);
        Assert.All(handler.Bodies, body =>
        {
            foreach (var propertyName in new[] { "id", "occurredAtUtc", "sourceCurrency", "targetCurrency", "debtId", "debtOperationType" })
                Assert.False(body.RootElement.TryGetProperty(propertyName, out _), $"Server-managed property {propertyName} was sent by the Web client.");
        });
    }

    private sealed class CapturingHandler(Guid id, Guid accountId) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public List<JsonDocument> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri?.PathAndQuery.TrimStart('/')}");
            Bodies.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (request.Method == HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.NoContent);
            var response = new TransactionDto(id, TransactionType.Adjustment, accountId, null, null, null, 10m, null, null, "HUF", null, AdjustmentDirection.Increase, new DateOnly(2026, 9, 2), new TimeOnly(10, 30), new DateTimeOffset(2026, 9, 2, 8, 30, 0, TimeSpan.Zero), null, null, "create", null, null, null);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(JsonSerializer.Serialize(response, JsonSerializerOptions.Web), Encoding.UTF8, "application/json") };
        }
    }
}
