using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PocketLedger.Api.Tests;

public sealed class OpenApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly IReadOnlyDictionary<string, string[]> RequiredOperations = new Dictionary<string, string[]>
    {
        ["/api/v1/accounts"] = ["get", "post"],
        ["/api/v1/accounts/{id}"] = ["get", "put", "delete"],
        ["/api/v1/accounts/{id}/balance"] = ["get"],
        ["/api/v1/accounts/{id}/deletion-summary"] = ["get"],
        ["/api/v1/accounts/{id}/recent-transactions"] = ["get"],
        ["/api/v1/accounts/balances"] = ["get"],
        ["/api/v1/accounts/choices"] = ["get"],
        ["/api/v1/calendar/{year}/{month}"] = ["get"],
        ["/api/v1/categories"] = ["get", "post"],
        ["/api/v1/categories/{id}"] = ["get", "put", "delete"],
        ["/api/v1/categories/choices"] = ["get"],
        ["/api/v1/debts"] = ["get", "post"],
        ["/api/v1/debts/{id}"] = ["get", "put", "delete"],
        ["/api/v1/debts/{id}/close"] = ["post"],
        ["/api/v1/debts/{id}/deletion-summary"] = ["get"],
        ["/api/v1/debts/{id}/operations"] = ["post"],
        ["/api/v1/debts/{id}/reopen"] = ["post"],
        ["/api/v1/debts/funding-warnings"] = ["get"],
        ["/api/v1/debts/operations/{transactionId}"] = ["get", "put", "delete"],
        ["/api/v1/import-export/backup"] = ["get"],
        ["/api/v1/import-export/csv/export"] = ["post"],
        ["/api/v1/import-export/csv/import"] = ["post"],
        ["/api/v1/import-export/csv/preview"] = ["post"],
        ["/api/v1/import-export/restore"] = ["post"],
        ["/api/v1/import-export/restore/preview"] = ["post"],
        ["/api/v1/preferences"] = ["get", "put"],
        ["/api/v1/recurring-transactions"] = ["get", "post"],
        ["/api/v1/recurring-transactions/{id}"] = ["get", "put", "delete"],
        ["/api/v1/statistics/currencies"] = ["get"],
        ["/api/v1/statistics/summary"] = ["get"],
        ["/api/v1/transactions"] = ["get", "post"],
        ["/api/v1/transactions/{id}"] = ["get", "put", "delete"],
        ["/api/v1/transactions/balances"] = ["get"],
        ["/api/v1/transactions/daily-totals"] = ["post"],
        ["/api/v1/transactions/export-query"] = ["post"],
        ["/api/v1/transactions/main-balance"] = ["get"],
        ["/api/v1/transactions/month"] = ["get"],
        ["/api/v1/transactions/recent"] = ["get"]
    };

    private readonly WebApplicationFactory<Program> factory;

    public OpenApiContractTests(WebApplicationFactory<Program> factory) => this.factory = factory.WithWebHostBuilder(builder =>
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false"));

    [Fact]
    public async Task Document_IsValidVersionedJson()
    {
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.StartsWith("3.", document.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("1.0.0", document.RootElement.GetProperty("info").GetProperty("version").GetString());
    }

    [Fact]
    public async Task Document_ContainsAllVersionOneOperations()
    {
        using var document = await GetDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        Assert.All(paths.EnumerateObject(), path => Assert.True(path.Name == "/health" || path.Name.StartsWith("/api/v1/", StringComparison.Ordinal), $"Unversioned application path found: {path.Name}"));
        foreach (var (path, methods) in RequiredOperations)
        {
            Assert.True(paths.TryGetProperty(path, out var pathItem), $"OpenAPI path is missing: {path}");
            foreach (var method in methods) Assert.True(pathItem.TryGetProperty(method, out _), $"OpenAPI operation is missing: {method.ToUpperInvariant()} {path}");
        }
    }

    [Fact]
    public async Task Document_DeclaresBearerAuthenticationForEveryOperation()
    {
        using var document = await GetDocumentAsync();
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        var bearer = schemes.GetProperty("Bearer");

        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        {
            if (path.Name == "/health")
            {
                Assert.False(operation.Value.TryGetProperty("security", out _));
                continue;
            }

            var security = operation.Value.GetProperty("security");
            Assert.Contains(security.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
        }
    }

    [Theory]
    [InlineData("TextPayload", "content")]
    [InlineData("AccountUpdateRequest", "account", "createInitialBalanceAdjustment")]
    [InlineData("DebtWriteRequest", "debt", "recurringPayment")]
    [InlineData("DebtOperationWriteRequest", "operation")]
    [InlineData("UserPreferenceUpdateRequest", "displayName", "avatarId", "defaultCurrency", "timeZoneId", "currencyFormats")]
    public async Task PublicContractSchema_ContainsRequiredProperties(string schemaName, params string[] propertyNames)
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"OpenAPI schema is missing: {schemaName}");

        var properties = schema.GetProperty("properties");
        foreach (var propertyName in propertyNames) Assert.True(properties.TryGetProperty(propertyName, out _), $"Property {schemaName}.{propertyName} is missing from the OpenAPI contract.");
    }

    [Theory]
    [InlineData("AccountDto", "ownerId")]
    [InlineData("CategoryDto", "ownerId")]
    [InlineData("DebtDto", "ownerId", "transactions", "recurringTransactions")]
    [InlineData("RecurringTransactionDto", "ownerId", "occurrences")]
    [InlineData("TransactionDto", "ownerId")]
    [InlineData("UserCurrencyFormatDto", "userId", "user")]
    public async Task PublicDto_DoesNotExposePersistenceProperties(string schemaName, params string[] forbiddenProperties)
    {
        using var document = await GetDocumentAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"OpenAPI schema is missing: {schemaName}");

        var properties = schema.GetProperty("properties");
        foreach (var propertyName in forbiddenProperties) Assert.False(properties.TryGetProperty(propertyName, out _), $"Persistence property {schemaName}.{propertyName} leaked into the OpenAPI contract.");
    }

    [Fact]
    public async Task Operations_DocumentTheSharedErrorContract()
    {
        using var document = await GetDocumentAsync();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        foreach (var operation in path.Value.EnumerateObject())
        foreach (var status in new[] { "400", "404", "500" })
        {
            var response = operation.Value.GetProperty("responses").GetProperty(status);
            var properties = response.GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("properties");
            Assert.True(properties.TryGetProperty("code", out _));
            Assert.True(properties.TryGetProperty("message", out _));
        }
    }

    private async Task<JsonDocument> GetDocumentAsync()
    {
        using var response = await factory.CreateClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    }
}
