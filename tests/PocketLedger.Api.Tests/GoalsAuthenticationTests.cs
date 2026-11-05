using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using PocketLedger.Security;

namespace PocketLedger.Api.Tests;

public sealed class GoalsAuthenticationTests
{
    [Theory]
    [InlineData("/api/v1/goals", false)]
    [InlineData("/api/v1/goals", true)]
    [InlineData("/api/v1/goals/7f3c2a54-3a53-4c55-9a55-0d6f3f6f1a11/contributions", false)]
    [InlineData("/api/v1/goals/7f3c2a54-3a53-4c55-9a55-0d6f3f6f1a11/contributions", true)]
    public async Task PostRequiresAuthorizationHeaderEvenWhenCookieContainsValidToken(string path, bool useBearer)
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:SigningKey", Convert.ToBase64String(key));
            builder.UseSetting("Authentication:Issuer", "https://goals-test.local/");
            // The block check reads the database on every authenticated request; this test must not need one.
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IUserAccessBlockStore>();
                services.AddSingleton<IUserAccessBlockStore>(new ActiveUserAccessStore());
            });
        });
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://goals-test.local/", audience: "pocketledger-api",
            claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"access_token={token}; .AspNetCore.Cookies={token}");
        if (useBearer) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Malformed JSON reaches model validation only after successful authentication, without touching the database.
        using var response = await client.PostAsync(path, new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(useBearer ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class ActiveUserAccessStore : IUserAccessBlockStore
    {
        public Task<UserAccessBlockState> IsBlockedAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(UserAccessBlockState.Active);
    }
}
