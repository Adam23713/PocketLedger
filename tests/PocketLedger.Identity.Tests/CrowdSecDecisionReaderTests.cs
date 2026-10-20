using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class CrowdSecDecisionReaderTests
{
    [Fact]
    public async Task GetActiveBans_UsesReadOnlyAuthenticatedEndpointAndReturnsOnlyIpAndRangeBans()
    {
        HttpMethod? method = null;
        Uri? requestUri = null;
        string? apiKey = null;
        var handler = new StubHandler(request =>
        {
            method = request.Method;
            requestUri = request.RequestUri;
            apiKey = request.Headers.GetValues("X-Api-Key").Single();
            const string json = """
                [
                  { "value": "203.0.113.10", "scope": "Ip", "scenario": "crowdsecurity/http-bad-user-agent", "origin": "crowdsec", "type": "ban", "duration": "2h15m" },
                  { "value": "2001:db8::/64", "scope": "Range", "scenario": "manual", "origin": "cscli", "type": "ban", "duration": "45m" },
                  { "value": "198.51.100.5", "scope": "Ip", "scenario": "challenge", "origin": "crowdsec", "type": "captcha", "duration": "10m" },
                  { "value": "HU", "scope": "Country", "scenario": "geo", "origin": "lists", "type": "ban", "duration": "1h" }
                ]
                """;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });
        var reader = CreateReader(handler, new CrowdSecOptions { BaseUrl = "http://crowdsec:8080/", ApiKey = "identity-secret" });

        var result = await reader.GetActiveBansAsync();

        Assert.True(result.IsAvailable);
        Assert.Equal(2, result.Decisions.Count);
        Assert.Contains(result.Decisions, item => item.Address == "203.0.113.10" && item.Scope == "Ip" && item.Action == "ban" && item.RemainingDuration == "2h15m");
        Assert.Contains(result.Decisions, item => item.Address == "2001:db8::/64" && item.Scope == "Range" && item.Scenario == "manual" && item.Origin == "cscli");
        Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("http://crowdsec:8080/v1/decisions?type=ban", requestUri?.OriginalString);
        Assert.Equal("identity-secret", apiKey);
    }

    [Fact]
    public async Task GetActiveBans_DoesNotCallLapiWithoutDedicatedConfiguration()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("LAPI must not be called."));
        var reader = CreateReader(handler, new CrowdSecOptions());

        var result = await reader.GetActiveBansAsync();

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Decisions);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task GetActiveBans_ConvertsLapiFailureToUnavailableState()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var reader = CreateReader(handler, new CrowdSecOptions { BaseUrl = "http://crowdsec:8080/", ApiKey = "identity-secret" });

        var result = await reader.GetActiveBansAsync();

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Decisions);
        Assert.Equal(1, handler.RequestCount);
    }

    private static CrowdSecDecisionReader CreateReader(HttpMessageHandler handler, CrowdSecOptions options)
        => new(new HttpClient(handler), Options.Create(options), NullLogger<CrowdSecDecisionReader>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
