using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using PocketLedger;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Models.Entities;
using PocketLedger.Middleware;
using PocketLedger.Services;
using PocketLedger.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDatabaseEncryption(builder.Configuration, builder.Environment, "PocketLedger.Identity");
builder.Services.AddControllersWithViews();
builder.Services.AddOptions<AuthenticationSecurityOptions>().BindConfiguration("Authentication").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<PublicRegistrationOptions>().BindConfiguration(PublicRegistrationOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ForwardedHeadersOptionsConfig>().BindConfiguration("ForwardedHeaders");
builder.Services.AddOptions<CrowdSecOptions>().BindConfiguration(CrowdSecOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddHttpClient<ICrowdSecDecisionReader, CrowdSecDecisionReader>((services, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<CrowdSecOptions>>().Value.TimeoutSeconds);
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityConnection"));
    options.UseOpenIddict();
});
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.User.RequireUniqueEmail = false;
    options.Password.RequiredLength = 14;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredUniqueChars = 8;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 3;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromHours(78);
}).AddEntityFrameworkStores<IdentityDbContext>().AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>().AddDefaultTokenProviders();
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication().AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.ClaimActions.MapJsonKey("email_verified", "email_verified", ClaimValueTypes.Boolean);
    });
}
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "PocketLedger.Identity";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Events.OnValidatePrincipal = BlockedUserCookieValidation.ValidatePrincipalAsync;
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(BootstrapAdministratorAuthorization.PolicyName, policy => policy.RequireClaim(BootstrapAdministratorAuthorization.ClaimType, bool.TrueString))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
if (builder.Environment.IsDevelopment()) builder.Services.AddCors(options => options.AddPolicy("Swagger", policy => policy.WithOrigins(builder.Configuration["OpenIddict:SwaggerBaseUrl"] ?? "http://localhost:5051").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddOpenIddict()
    .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(builder.Configuration["OpenIddict:Issuer"] ?? "https://identity.localhost/"));
        options.SetAuthorizationEndpointUris("connect/authorize");
        options.SetTokenEndpointUris("connect/token");
        options.SetEndSessionEndpointUris("connect/logout");
        options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow();
        options.RequireProofKeyForCodeExchange();
        options.RegisterScopes(OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.OfflineAccess, "pocketledger.api");
        var signingKey = builder.Configuration["OpenIddict:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            if (!builder.Environment.IsDevelopment()) throw new InvalidOperationException("OpenIddict:SigningKey is required outside Development.");
            signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        }
        options.AddSigningKey(new SymmetricSecurityKey(Convert.FromBase64String(signingKey)));
        options.AddEphemeralSigningKey();
        options.AddEphemeralEncryptionKey();
        options.DisableAccessTokenEncryption();
        var aspNetCore = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableEndSessionEndpointPassthrough();
        if (builder.Environment.IsDevelopment()) aspNetCore.DisableTransportSecurityRequirement();
    });
builder.Services.AddSingleton<IAuthenticationRateLimiter, AuthenticationRateLimiter>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<UserKeyDerivationLimiter>();
builder.Services.AddScoped<IUserDataKeyService, UserDataKeyService>();
builder.Services.AddSingleton<IPendingSignInKeys, PendingSignInKeys>();
builder.Services.AddSingleton<ISessionKeyHandoff, SessionKeyHandoff>();
builder.Services.AddSingleton<IClientIpAddressResolver, ClientIpAddressResolver>();
builder.Services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
builder.Services.AddScoped<IRegistrationSettingsService, RegistrationSettingsService>();
builder.Services.AddHostedService<OpenIddictClientSeeder>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddRequestTelemetry(builder.Configuration);
builder.Services.AddHttpClient<IAdminUserDataClient, AdminUserDataClient>(client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5051/"))
    .ConfigurePrimaryHttpMessageHandler(() => InternalApiTls.CreateHandler(builder.Configuration, builder.Environment));
builder.Services.AddHttpClient(UserAccessPropagation.ApiClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5051/");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => InternalApiTls.CreateHandler(builder.Configuration, builder.Environment));
builder.Services.AddHttpClient(UserAccessPropagation.WebClientName, client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Web:InternalBaseUrl"] ?? "http://localhost:5050/");
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IUserAccessPropagation, UserAccessPropagation>();
builder.Services.AddHostedService<UserAccessBlockSynchronizer>();

builder.Services.CompleteDatabaseEncryptionRegistration(builder.Configuration);
var app = builder.Build();
if (args is [KeyRingMigrationCommand.Name])
{
    Environment.ExitCode = KeyRingMigrationCommand.Run(app.Services);
    return;
}
if (args.Length > 0 && (args[0] == "bootstrap-identity" || args[0] == "account"))
{
    Environment.ExitCode = await CommandRunner.RunAsync(args, app.Services);
    return;
}
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
}
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAll")) { forwarded.KnownIPNetworks.Clear(); forwarded.KnownProxies.Clear(); }
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? []) if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
app.UseForwardedHeaders(forwarded);
app.UseEncryptionReadinessGate();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseStaticFiles();
app.UseRouting();
if (app.Environment.IsDevelopment()) app.UseCors("Swagger");
app.UseAuthentication();
app.UseBlockedUserRedirect();
app.UseRequestTelemetry();
app.UseMiddleware<MandatoryTwoFactorMiddleware>();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapEncryptionHealthEndpoints();
app.Run();

public partial class Program;
