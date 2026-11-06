using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PocketLedger.Configuration;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class CrowdSecBanManagerTests
{
    private static readonly CrowdSecOptions Configured = new() { BaseUrl = "http://crowdsec:8080/", MachineId = "pocketledger-identity", MachinePassword = "machine-secret" };

    [Fact]
    public async Task Ban_LogsInAndCreatesATimedIpDecision()
    {
        var requests = new List<(HttpMethod Method, string Uri, string? Authorization, string Body)>();
        var manager = CreateManager(Configured, requests);

        Assert.True(await manager.BanAsync("203.0.113.10", TimeSpan.FromHours(168)));

        Assert.Equal(2, requests.Count);
        Assert.Equal("http://crowdsec:8080/v1/watchers/login", requests[0].Uri);
        using var login = JsonDocument.Parse(requests[0].Body);
        Assert.Equal("pocketledger-identity", login.RootElement.GetProperty("machine_id").GetString());
        Assert.Equal("machine-secret", login.RootElement.GetProperty("password").GetString());

        Assert.Equal((HttpMethod.Post, "http://crowdsec:8080/v1/alerts", "Bearer jwt-token"), (requests[1].Method, requests[1].Uri, requests[1].Authorization));
        using var alerts = JsonDocument.Parse(requests[1].Body);
        var decision = alerts.RootElement[0].GetProperty("decisions")[0];
        Assert.Equal("ban", decision.GetProperty("type").GetString());
        Assert.Equal("Ip", decision.GetProperty("scope").GetString());
        Assert.Equal("203.0.113.10", decision.GetProperty("value").GetString());
        Assert.Equal("168h", decision.GetProperty("duration").GetString());
        Assert.Equal("Ip", alerts.RootElement[0].GetProperty("source").GetProperty("scope").GetString());
    }

    [Theory]
    [InlineData("203.0.113.10", "http://crowdsec:8080/v1/decisions?scopes=Ip&value=203.0.113.10")]
    [InlineData("2001:db8::/64", "http://crowdsec:8080/v1/decisions?scopes=Range&value=2001%3Adb8%3A%3A%2F64")]
    public async Task Remove_DeletesOnlyTheExactValue(string address, string expectedUri)
    {
        var requests = new List<(HttpMethod Method, string Uri, string? Authorization, string Body)>();
        var manager = CreateManager(Configured, requests);

        Assert.True(await manager.RemoveAsync(address));

        Assert.Equal((HttpMethod.Delete, expectedUri, "Bearer jwt-token"), (requests[1].Method, requests[1].Uri, requests[1].Authorization));
    }

    [Fact]
    public async Task EveryRequest_SendsTheUserAgentFormatLapiRequiresForMachineLogin()
    {
        var userAgents = new List<string>();
        var manager = new CrowdSecBanManager(new HttpClient(new UserAgentHandler(userAgents)), Options.Create(Configured), TimeProvider.System, NullLogger<CrowdSecBanManager>.Instance);

        Assert.True(await manager.BanAsync("203.0.113.10", TimeSpan.FromHours(1)));

        Assert.Equal(2, userAgents.Count);
        Assert.All(userAgents, value => Assert.Matches("^[^/ ]+/[^/ ]+$", value));
    }

    [Fact]
    public async Task WithoutMachineAccount_NothingIsSent()
    {
        var requests = new List<(HttpMethod Method, string Uri, string? Authorization, string Body)>();
        var manager = CreateManager(new CrowdSecOptions { BaseUrl = "http://crowdsec:8080/", ApiKey = "read-only" }, requests);

        Assert.False(manager.IsConfigured);
        Assert.False(await manager.BanAsync("203.0.113.10", TimeSpan.FromHours(1)));
        Assert.Empty(requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.OK, HttpStatusCode.InternalServerError)]
    public async Task LapiFailure_IsReportedWithoutThrowing(HttpStatusCode loginStatus, HttpStatusCode actionStatus)
    {
        var manager = CreateManager(Configured, [], loginStatus, actionStatus);

        Assert.False(await manager.BanAsync("203.0.113.10", TimeSpan.FromHours(1)));
        Assert.False(await manager.RemoveAsync("203.0.113.10"));
    }

    [Theory]
    [InlineData("203.0.113.10", ClientAddressKind.Public)]
    [InlineData("2001:db8::1", ClientAddressKind.Public)]
    [InlineData("10.1.2.3", ClientAddressKind.Internal)]
    [InlineData("172.18.0.5", ClientAddressKind.Internal)]
    [InlineData("192.168.1.1", ClientAddressKind.Internal)]
    [InlineData("127.0.0.1", ClientAddressKind.Internal)]
    [InlineData("100.64.0.1", ClientAddressKind.Internal)]
    [InlineData("::1", ClientAddressKind.Internal)]
    [InlineData("fd00::1", ClientAddressKind.Internal)]
    [InlineData("fe80::1", ClientAddressKind.Internal)]
    [InlineData("::ffff:10.1.2.3", ClientAddressKind.Internal)]
    [InlineData("173.245.48.1", ClientAddressKind.CloudflareEdge)]
    [InlineData("2606:4700::1", ClientAddressKind.CloudflareEdge)]
    public void Policy_ClassifiesAddresses(string address, ClientAddressKind expected)
        => Assert.Equal(expected, ClientAddressPolicy.Classify(address));

    [Fact]
    public void Policy_RefusesProtectedAddressesAndAllowsOthers()
    {
        Assert.Null(ClientAddressPolicy.GetBanRefusal("203.0.113.10", false));
        Assert.Contains("you are using", ClientAddressPolicy.GetBanRefusal("203.0.113.10", true));
        Assert.Contains("internal", ClientAddressPolicy.GetBanRefusal("10.1.2.3", false));
        Assert.Contains("Cloudflare", ClientAddressPolicy.GetBanRefusal("173.245.48.1", false));
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10", true)]
    [InlineData("203.0.113.10", "203.0.113.0/24", true)]
    [InlineData("203.0.113.10", "203.0.113.11", false)]
    [InlineData("203.0.113.10", "198.51.100.0/24", false)]
    [InlineData("2001:db8::1", "2001:db8::/64", true)]
    public void Policy_FindsBansCoveringAnAddress(string address, string ban, bool expected)
        => Assert.Equal(expected, ClientAddressPolicy.IsCoveredBy(address, ban));

    [Fact]
    public void CloudflareNetworks_MatchTheCaddyTrustList()
    {
        var caddyfile = File.ReadAllText(FindRepositoryFile("Caddyfile.example"));
        var line = Regex.Match(caddyfile, @"trusted_proxies static (.+)").Groups[1].Value;

        Assert.Equal(line.Trim().Split(' ').Order(), ClientAddressPolicy.CloudflareNetworks.Select(network => network.ToString()).Order());
    }

    [Fact]
    public void CaddyTemplate_SendsOnlyTheVerifiedClientAddressToEveryRecordingApplication()
    {
        var caddyfile = File.ReadAllText(FindRepositoryFile("Caddyfile.example"));

        foreach (var route in new[] { "@web", "@api", "@identity" })
            Assert.Matches(new Regex($@"reverse_proxy {route} \S+ \{{\s*header_up X-Forwarded-For \{{client_ip\}}"), caddyfile);
    }

    private static string FindRepositoryFile(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, name);
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException($"{name} was not found above the test directory.");
    }

    private static CrowdSecBanManager CreateManager(CrowdSecOptions options, List<(HttpMethod, string, string?, string)> requests, HttpStatusCode loginStatus = HttpStatusCode.OK, HttpStatusCode actionStatus = HttpStatusCode.OK)
        => new(new HttpClient(new RecordingHandler(requests, loginStatus, actionStatus)), Options.Create(options), TimeProvider.System, NullLogger<CrowdSecBanManager>.Instance);

    private sealed class UserAgentHandler(List<string> userAgents) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            userAgents.Add(request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"token":"jwt-token"}""", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class RecordingHandler(List<(HttpMethod, string, string?, string)> requests, HttpStatusCode loginStatus, HttpStatusCode actionStatus) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.OriginalString;
            requests.Add((request.Method, uri, request.Headers.Authorization?.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            if (uri.EndsWith("/watchers/login", StringComparison.Ordinal))
                return new HttpResponseMessage(loginStatus) { Content = new StringContent("""{"token":"jwt-token"}""", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(actionStatus);
        }
    }
}
