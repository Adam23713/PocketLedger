using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Security;
using PocketLedger.Services;

namespace PocketLedger.Identity.Tests;

public sealed class UserDataKeyFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Username = "data-key-user";
    private const string Password = "DataKey!Password#2026";
    private const string NewPassword = "DataKey!Replaced#2027";
    // With PL_TEST_POSTGRES set (as in CI) these flows run on PostgreSQL with migrations, which exercises their transactions.
    private static readonly string? Postgres = Environment.GetEnvironmentVariable("PL_TEST_POSTGRES");
    private readonly string databaseName = "pl_identity_keys_" + Guid.NewGuid().ToString("N");
    private readonly WebApplicationFactory<Program> factory;

    public UserDataKeyFlowTests(WebApplicationFactory<Program> factory)
    {
        // The host's hosted services query the database at startup, so it must exist and be migrated first.
        if (!string.IsNullOrWhiteSpace(Postgres))
        {
            using var setup = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(PostgresConnectionString).UseOpenIddict().Options);
            setup.Database.Migrate();
        }
        this.factory = Configure(factory);
    }

    private string PostgresConnectionString => new Npgsql.NpgsqlConnectionStringBuilder(Postgres) { Database = databaseName }.ConnectionString;

    private WebApplicationFactory<Program> Configure(WebApplicationFactory<Program> factory) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
        builder.UseSetting("Registration:Enabled", "true");
        builder.UseSetting("Authentication:RateLimitPermitCount", "20");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options =>
            {
                if (string.IsNullOrWhiteSpace(Postgres)) options.UseInMemoryDatabase(databaseName);
                else options.UseNpgsql(PostgresConnectionString);
                options.UseOpenIddict();
            });
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, VerifiedExternalAuthenticationHandler>(VerifiedExternalAuthenticationHandler.SchemeName, "Verified provider", _ => { });
        });
    });

    public void Dispose()
    {
        factory.Dispose();
        if (string.IsNullOrWhiteSpace(Postgres)) return;
        Npgsql.NpgsqlConnection.ClearAllPools();
        using var admin = new Npgsql.NpgsqlConnection(Postgres);
        admin.Open();
        using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", admin);
        drop.ExecuteNonQuery();
    }

    [Fact]
    public async Task PasswordLogin_UnlocksTheDataKeyAndCompletesTwoFactor()
    {
        var seeded = await SeedUserAsync();
        using var client = CreateClient();

        var loginResponse = await LoginAsync(client, Password);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.StartsWith("/Account/TwoFactor", loginResponse.Headers.Location?.OriginalString);
        Assert.Contains(loginResponse.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(PendingSignInKeys.CookieName + "=", StringComparison.Ordinal));

        var twoFactorResponse = await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey);
        Assert.Equal(HttpStatusCode.Redirect, twoFactorResponse.StatusCode);
        Assert.Equal("http://localhost:5050/Session/CompleteSignIn", twoFactorResponse.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TwoFactor_WithoutAPendingDataKey_RestartsTheSignIn()
    {
        var seeded = await SeedUserAsync();
        using var client = CreateClient();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, Password)).StatusCode);
        ((MemoryCache)factory.Services.GetRequiredService<IMemoryCache>()).Clear();

        var response = await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location?.OriginalString);
        Assert.Contains("Your sign-in expired", await client.GetStringAsync(response.Headers.Location));
        Assert.StartsWith("/Account/Login", (await client.GetAsync("/Account/Security")).Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task PasswordLogin_WithoutADataKey_FailsClosed()
    {
        var seeded = await SeedUserAsync(withDataKey: false);
        using var client = CreateClient();

        var response = await LoginAsync(client, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your data could not be unlocked", await response.Content.ReadAsStringAsync());
        // The pending two-factor sign-in was discarded, so even a valid authenticator code cannot finish it.
        var twoFactor = await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey);
        Assert.Equal(HttpStatusCode.OK, twoFactor.StatusCode);
        Assert.Contains("Invalid authentication attempt.", await twoFactor.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "DataKeyUnlock" && item.Outcome == "Failure" && item.FailureReason == "DataKeyMissing");
    }

    [Fact]
    public async Task RecoverAccount_ReplacesThePasswordAndRecoveryKeyButKeepsTheDataKey()
    {
        var seeded = await SeedUserAsync();
        using var client = CreateClient();

        var response = await RecoverAsync(client, seeded.RecoveryKey.Replace("-", " ").ToLowerInvariant(), NewPassword);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Your password has been replaced", html);
        Assert.Contains("action=\"/Account/Login\"", html);
        var newRecoveryKey = ExtractRecoveryKey(html);
        Assert.NotEqual(seeded.RecoveryKey, newRecoveryKey);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync(Username))!;
        Assert.False(await users.CheckPasswordAsync(user, Password));
        Assert.True(await users.CheckPasswordAsync(user, NewPassword));
        var dataKeys = scope.ServiceProvider.GetRequiredService<IUserDataKeyService>();
        await Assert.ThrowsAsync<UserKeyException>(() => dataKeys.UnlockAsync(user.Id, Password, UserKeySecretKind.Password));
        await Assert.ThrowsAsync<UserKeyException>(() => dataKeys.UnlockAsync(user.Id, seeded.RecoveryKey, UserKeySecretKind.RecoveryKey));
        using var byPassword = await dataKeys.UnlockAsync(user.Id, NewPassword, UserKeySecretKind.Password);
        using var byRecoveryKey = await dataKeys.UnlockAsync(user.Id, newRecoveryKey, UserKeySecretKind.RecoveryKey);
        Assert.Equal("seeded before recovery", UserFieldCipher.DecryptString(seeded.Ciphertext, byPassword!, "Note"));
        Assert.Equal("seeded before recovery", UserFieldCipher.DecryptString(seeded.Ciphertext, byRecoveryKey!, "Note"));
        Assert.Contains(await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthenticationAuditEvents.ToListAsync(),
            item => item.EventType == "AccountRecovery" && item.Outcome == "Success" && item.UserId == user.Id);
    }

    [Fact]
    public async Task RecoverAccount_WithAWrongKey_IsRejectedAndCountsTowardLockout()
    {
        await SeedUserAsync();
        using var client = CreateClient();

        var response = await RecoverAsync(client, RecoveryKey.Generate(), NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Account recovery was not completed", await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync(Username))!;
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.Equal(1, await users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task RecoverAccount_ForAnUnknownUser_GivesTheSameAnswer()
    {
        await SeedUserAsync();
        using var client = CreateClient();
        var token = await GetAntiforgeryTokenAsync(client, "/Account/RecoverAccount");

        var response = await client.PostAsync("/Account/RecoverAccount", Form(new Dictionary<string, string>
        {
            ["Username"] = "nobody",
            ["RecoveryKey"] = RecoveryKey.Generate(),
            ["NewPassword"] = NewPassword,
            ["ConfirmPassword"] = NewPassword,
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Account recovery was not completed", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExternalUser_ChoosesADataPasswordAndUnlocksWithItOnReturn()
    {
        await ResetDatabaseAsync();
        string recoveryKey;
        using (var firstClient = CreateClient())
        {
            var callback = await CompleteExternalLoginAsync(firstClient);
            Assert.StartsWith("/Account/CreateDataPassword", callback.Headers.Location?.OriginalString);
            var token = ExtractAntiforgeryToken(await firstClient.GetStringAsync(callback.Headers.Location));

            var weak = await firstClient.PostAsync("/Account/CreateDataPassword", Form(new Dictionary<string, string>
            {
                ["Password"] = "aaaaaaaaaaaaaaaa",
                ["ConfirmPassword"] = "aaaaaaaaaaaaaaaa",
                ["__RequestVerificationToken"] = token
            }));
            Assert.Equal(HttpStatusCode.OK, weak.StatusCode);
            Assert.DoesNotContain("data-recovery-key", await weak.Content.ReadAsStringAsync());

            var created = await firstClient.PostAsync("/Account/CreateDataPassword", Form(new Dictionary<string, string>
            {
                ["Password"] = Password,
                ["ConfirmPassword"] = Password,
                ["__RequestVerificationToken"] = token
            }));
            var html = await created.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);
            Assert.Contains("action=\"/Account/SetupAuthenticator\"", html);
            Assert.Contains(created.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(PendingSignInKeys.CookieName + "=", StringComparison.Ordinal));
            recoveryKey = ExtractRecoveryKey(html);
        }

        using var returningClient = CreateClient();
        var returning = await CompleteExternalLoginAsync(returningClient);
        Assert.StartsWith("/Account/UnlockData", returning.Headers.Location?.OriginalString);
        var unlockHtml = await returningClient.GetStringAsync(returning.Headers.Location);
        Assert.Contains("data password", unlockHtml);

        var wrong = await returningClient.PostAsync("/Account/UnlockData", Form(new Dictionary<string, string>
        {
            ["Password"] = "Wrong!DataPassword#1",
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(unlockHtml)
        }));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Incorrect data password.", await wrong.Content.ReadAsStringAsync());

        var unlocked = await returningClient.PostAsync("/Account/UnlockData", Form(new Dictionary<string, string>
        {
            ["Password"] = Password,
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(unlockHtml)
        }));
        Assert.Equal(HttpStatusCode.Redirect, unlocked.StatusCode);
        Assert.StartsWith("/Account/SetupAuthenticator", unlocked.Headers.Location?.OriginalString);
        Assert.Contains(unlocked.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(PendingSignInKeys.CookieName + "=", StringComparison.Ordinal));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByLoginAsync(VerifiedExternalAuthenticationHandler.SchemeName, VerifiedExternalAuthenticationHandler.Subject))!;
        Assert.False(await users.HasPasswordAsync(user));
        Assert.Equal(0, await users.GetAccessFailedCountAsync(user));
        using var byRecoveryKey = await scope.ServiceProvider.GetRequiredService<IUserDataKeyService>().UnlockAsync(user.Id, recoveryKey, UserKeySecretKind.RecoveryKey);
        Assert.NotNull(byRecoveryKey);
    }

    [Fact]
    public async Task RegenerateRecoveryKey_RequiresThePasswordAndRetiresThePreviousKey()
    {
        var seeded = await SeedUserAsync();
        using var client = CreateClient();
        await LoginAsync(client, Password);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey)).StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, "/Account/RegenerateRecoveryKey");

        var rejected = await client.PostAsync("/Account/RegenerateRecoveryKey", Form(new Dictionary<string, string>
        {
            ["CurrentPassword"] = "Wrong!Password#2026",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Contains("The recovery key was not replaced", await rejected.Content.ReadAsStringAsync());

        var replaced = await client.PostAsync("/Account/RegenerateRecoveryKey", Form(new Dictionary<string, string>
        {
            ["CurrentPassword"] = Password,
            ["__RequestVerificationToken"] = token
        }));
        var html = await replaced.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Contains("action=\"/Account/Security\"", html);
        var newRecoveryKey = ExtractRecoveryKey(html);

        await using var scope = factory.Services.CreateAsyncScope();
        var userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(Username))!.Id;
        var dataKeys = scope.ServiceProvider.GetRequiredService<IUserDataKeyService>();
        await Assert.ThrowsAsync<UserKeyException>(() => dataKeys.UnlockAsync(userId, seeded.RecoveryKey, UserKeySecretKind.RecoveryKey));
        using var byRecoveryKey = await dataKeys.UnlockAsync(userId, newRecoveryKey, UserKeySecretKind.RecoveryKey);
        Assert.Equal("seeded before recovery", UserFieldCipher.DecryptString(seeded.Ciphertext, byRecoveryKey!, "Note"));
    }

    [Fact]
    public async Task CodeExchange_HandsTheSessionDataKeyToTheClientOnceAndRefreshKeepsTheSession()
    {
        var seeded = await SeedUserAsync();
        await RegisterTestClientAsync();
        using var client = CreateClient();
        await LoginAsync(client, Password);
        Assert.Equal(HttpStatusCode.Redirect, (await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey)).StatusCode);

        var (code, verifier) = await AuthorizeAsync(client);
        using var tokens = await RedeemAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = TestRedirectUri,
            ["client_id"] = TestClientId,
            ["code_verifier"] = verifier
        });

        var root = tokens.RootElement;
        var accessClaims = ReadJwtPayload(root.GetProperty("access_token").GetString()!);
        var sessionId = accessClaims.GetProperty(UserSessionKeyProtocol.SessionIdClaim).GetString();
        Assert.False(accessClaims.TryGetProperty(UserSessionKeyProtocol.HandoffClaim, out _));
        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
            userId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(Username))!.Id;
        using (var dataKey = UserSessionKeyProtocol.TryUnwrap(root.GetProperty(UserSessionKeyProtocol.SessionKeyParameter).GetString(), root.GetProperty(UserSessionKeyProtocol.WrappedKeyParameter).GetString(), userId, sessionId))
        {
            Assert.NotNull(dataKey);
            Assert.Equal("seeded before recovery", UserFieldCipher.DecryptString(seeded.Ciphertext, dataKey, "Note"));
        }
        Assert.Null(UserSessionKeyProtocol.TryUnwrap(root.GetProperty(UserSessionKeyProtocol.SessionKeyParameter).GetString(), root.GetProperty(UserSessionKeyProtocol.WrappedKeyParameter).GetString(), userId, "another-session"));

        using var refreshed = await RedeemAsync(client, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = root.GetProperty("refresh_token").GetString()!,
            ["client_id"] = TestClientId
        });
        Assert.False(refreshed.RootElement.TryGetProperty(UserSessionKeyProtocol.SessionKeyParameter, out _));
        Assert.Equal(sessionId, ReadJwtPayload(refreshed.RootElement.GetProperty("access_token").GetString()!).GetProperty(UserSessionKeyProtocol.SessionIdClaim).GetString());

        // The pending key was used up; a second client session needs the password again even though the Identity cookie is valid.
        var again = await client.GetAsync(AuthorizePath(CreateCodeChallenge(CreateCodeVerifier())));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        Assert.StartsWith("/Account/UnlockData", again.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CodeExchange_WithoutPreparedSessionMaterial_IsRefused()
    {
        var seeded = await SeedUserAsync();
        await RegisterTestClientAsync();
        using var client = CreateClient();
        await LoginAsync(client, Password);
        await SubmitTwoFactorAsync(client, seeded.AuthenticatorKey);
        var (code, verifier) = await AuthorizeAsync(client);
        ((MemoryCache)factory.Services.GetRequiredService<IMemoryCache>()).Clear();

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = TestRedirectUri,
            ["client_id"] = TestClientId,
            ["code_verifier"] = verifier
        }));

        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(UserSessionKeyProtocol.SessionKeyParameter, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DerivationLimiter_RejectsWorkWhenEverySlotStaysBusy()
    {
        using var limiter = new UserKeyDerivationLimiter(Options.Create(new AuthenticationSecurityOptions { DataKeyDerivationConcurrency = 1, DataKeyDerivationWaitSeconds = 1 }));
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource();
        var first = Task.Run(() => limiter.RunAsync(() => { started.SetResult(); release.Wait(); return 1; }, CancellationToken.None));
        await started.Task;

        await Assert.ThrowsAsync<UserKeyDerivationBusyException>(() => limiter.RunAsync(() => 2, CancellationToken.None));
        release.Set();
        Assert.Equal(1, await first);
        Assert.Equal(3, await limiter.RunAsync(() => 3, CancellationToken.None));
    }

    private async Task<SeededUser> SeedUserAsync(bool withDataKey = true)
    {
        await ResetDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = Username, Email = "data-key-user@example.test" };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        user.AuthenticatorSetupComplete = true;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
        var authenticatorKey = (await users.GetAuthenticatorKeyAsync(user))!;
        if (!withDataKey) return new SeededUser(authenticatorKey, string.Empty, string.Empty);
        using var created = await scope.ServiceProvider.GetRequiredService<IUserDataKeyService>().CreateAsync(user.Id, Password);
        return new SeededUser(authenticatorKey, created.RecoveryKey, UserFieldCipher.EncryptString("seeded before recovery", created.Key, "Note"));
    }

    private sealed record SeededUser(string AuthenticatorKey, string RecoveryKey, string Ciphertext);

    private const string TestClientId = "data-key-test-client";
    private const string TestRedirectUri = "http://localhost:5050/signin-oidc";

    private async Task RegisterTestClientAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = TestClientId,
            ClientType = OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            RedirectUris = { new Uri(TestRedirectUri) },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + "pocketledger.api"
            },
            Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
        });
    }

    private static async Task<(string Code, string Verifier)> AuthorizeAsync(HttpClient client)
    {
        var verifier = CreateCodeVerifier();
        var response = await client.GetAsync(AuthorizePath(CreateCodeChallenge(verifier)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(TestRedirectUri, location.OriginalString);
        var code = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(location.Query)["code"].ToString();
        Assert.False(string.IsNullOrEmpty(code), location.OriginalString);
        return (code, verifier);
    }

    private static string AuthorizePath(string challenge)
        => $"/connect/authorize?client_id={TestClientId}&response_type=code&redirect_uri={Uri.EscapeDataString(TestRedirectUri)}&scope={Uri.EscapeDataString("openid offline_access pocketledger.api")}&code_challenge={challenge}&code_challenge_method=S256&state=test-state";

    private static async Task<JsonDocument> RedeemAsync(HttpClient client, Dictionary<string, string> form)
    {
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(form));
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, content);
        return JsonDocument.Parse(content);
    }

    private static JsonElement ReadJwtPayload(string token)
        => JsonDocument.Parse(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token.Split('.')[1])).RootElement.Clone();

    private static string CreateCodeVerifier() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string verifier) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));

    private async Task ResetDatabaseAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database;
        if (!database.IsRelational())
        {
            await database.EnsureDeletedAsync();
            return;
        }
        // Empty every table but keep the migrated schema, matching a deleted in-memory database.
        await database.ExecuteSqlRawAsync("""
            DO $$ DECLARE item record; BEGIN
                FOR item IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory' LOOP
                    EXECUTE 'TRUNCATE TABLE ' || quote_ident(item.tablename) || ' CASCADE';
                END LOOP;
            END $$;
            """);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        return await client.PostAsync("/Account/Login", Form(new Dictionary<string, string>
        {
            ["Username"] = Username,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
    }

    private static async Task<HttpResponseMessage> SubmitTwoFactorAsync(HttpClient client, string authenticatorKey)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/TwoFactor");
        return await client.PostAsync("/Account/TwoFactor", Form(new Dictionary<string, string>
        {
            ["Code"] = GenerateAuthenticatorCode(authenticatorKey),
            ["__RequestVerificationToken"] = token
        }));
    }

    private static async Task<HttpResponseMessage> RecoverAsync(HttpClient client, string recoveryKey, string newPassword)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/RecoverAccount");
        return await client.PostAsync("/Account/RecoverAccount", Form(new Dictionary<string, string>
        {
            ["Username"] = Username,
            ["RecoveryKey"] = recoveryKey,
            ["NewPassword"] = newPassword,
            ["ConfirmPassword"] = newPassword,
            ["__RequestVerificationToken"] = token
        }));
    }

    private static async Task<HttpResponseMessage> CompleteExternalLoginAsync(HttpClient client)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/Account/Login");
        var challenge = await client.PostAsync("/Account/ExternalLogin", Form(new Dictionary<string, string>
        {
            ["provider"] = VerifiedExternalAuthenticationHandler.SchemeName,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var callback = await client.GetAsync(challenge.Headers.Location);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return callback;
    }

    private static string ExtractRecoveryKey(string html)
    {
        var match = Regex.Match(html, "<code data-recovery-key>([^<]+)</code>");
        Assert.True(match.Success, html);
        return match.Groups[1].Value;
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path) => ExtractAntiforgeryToken(await client.GetStringAsync(path));

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success, html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values)
    {
        var content = new FormUrlEncodedContent(values);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        return content;
    }

    private static string GenerateAuthenticatorCode(string sharedKey)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        using var hmac = new HMACSHA1(DecodeBase32(sharedKey));
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binaryCode = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in value.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var index = alphabet.IndexOf(character);
            if (index < 0) throw new FormatException("Invalid Base32 authenticator key.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return bytes.ToArray();
    }
}

public sealed class PendingSignInKeysTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void IssuedKey_OpensOnlyForTheSameUser()
    {
        var (store, _) = CreateStore();
        using var key = UserDataKey.Generate(UserId);
        var ciphertext = UserFieldCipher.EncryptString("pending", key, "Note");
        var cookie = Issue(store, key);

        using var opened = store.TryOpen(WithCookie(cookie), UserId);
        Assert.NotNull(opened);
        Assert.Equal("pending", UserFieldCipher.DecryptString(ciphertext, opened, "Note"));
        Assert.Null(store.TryOpen(WithCookie(cookie), Guid.NewGuid()));
        Assert.Null(store.TryOpen(new DefaultHttpContext(), UserId));
    }

    [Fact]
    public void TamperedCookie_IsIgnored()
    {
        var (store, _) = CreateStore();
        using var key = UserDataKey.Generate(UserId);
        var cookie = Issue(store, key);
        var tampered = cookie[..^2] + (cookie[^2] == 'A' ? "B" : "A") + cookie[^1];

        Assert.Null(store.TryOpen(WithCookie(tampered), UserId));
        Assert.Null(store.TryOpen(WithCookie("not-a-cookie"), UserId));
    }

    [Fact]
    public void ExpiredCookie_IsIgnored()
    {
        var (store, clock) = CreateStore();
        using var key = UserDataKey.Generate(UserId);
        var cookie = Issue(store, key);

        clock.Advance(PendingSignInKeys.Lifetime + TimeSpan.FromSeconds(1));

        Assert.Null(store.TryOpen(WithCookie(cookie), UserId));
    }

    [Fact]
    public void Clear_RemovesTheServerSideKeyAndDeletesTheCookie()
    {
        var (store, _) = CreateStore();
        using var key = UserDataKey.Generate(UserId);
        var cookie = Issue(store, key);
        var context = WithCookie(cookie);

        store.Clear(context);

        Assert.Contains(PendingSignInKeys.CookieName + "=;", context.Response.Headers.SetCookie.ToString());
        Assert.Null(store.TryOpen(WithCookie(cookie), UserId));
    }

    [Fact]
    public void ReissuingReplacesThePreviousKey()
    {
        var (store, _) = CreateStore();
        using var key = UserDataKey.Generate(UserId);
        var first = Issue(store, key);
        var context = WithCookie(first);

        store.Issue(context, key);
        var second = ReadIssuedCookie(context);

        Assert.Null(store.TryOpen(WithCookie(first), UserId));
        Assert.True(store.Has(WithCookie(second), UserId));
    }

    private static (PendingSignInKeys Store, MutableTimeProvider Clock) CreateStore()
    {
        var clock = new MutableTimeProvider();
        return (new PendingSignInKeys(new MemoryCache(new MemoryCacheOptions()), new EphemeralDataProtectionProvider(), new TestHostEnvironment(), clock), clock);
    }

    private static string Issue(PendingSignInKeys store, UserDataKey key)
    {
        var context = new DefaultHttpContext();
        store.Issue(context, key);
        return ReadIssuedCookie(context);
    }

    private static string ReadIssuedCookie(HttpContext context)
    {
        var header = context.Response.Headers.SetCookie.Last(value => value!.StartsWith(PendingSignInKeys.CookieName + "=", StringComparison.Ordinal) && !value.StartsWith(PendingSignInKeys.CookieName + "=;", StringComparison.Ordinal))!;
        var value = header[(PendingSignInKeys.CookieName.Length + 1)..];
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        return value[..value.IndexOf(';')];
    }

    private static HttpContext WithCookie(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{PendingSignInKeys.CookieName}={value}";
        return context;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "PocketLedger.Identity.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
