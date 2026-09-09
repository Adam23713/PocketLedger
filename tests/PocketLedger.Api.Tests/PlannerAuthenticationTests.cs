using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace PocketLedger.Api.Tests;

public sealed class PlannerAuthenticationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateRequiresAuthorizationHeaderEvenWhenCookieContainsValidToken(bool useBearer)
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("Authentication:SigningKey", Convert.ToBase64String(key));
            builder.UseSetting("Authentication:Issuer", "https://planner-test.local/");
        });
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "https://planner-test.local/", audience: "pocketledger-api",
            claims: [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256)));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"access_token={token}; .AspNetCore.Cookies={token}");
        if (useBearer) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        // Malformed JSON reaches model validation only after successful authentication, without touching the database.
        using var response = await client.PostAsync("/api/v1/planner/items", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(useBearer ? HttpStatusCode.BadRequest : HttpStatusCode.Unauthorized, response.StatusCode);
        if (useBearer) Assert.Contains("validation errors", await response.Content.ReadAsStringAsync());
    }
}
