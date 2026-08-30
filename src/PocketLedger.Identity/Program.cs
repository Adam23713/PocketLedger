using System.Net;
using System.Security.Cryptography;
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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddOptions<AuthenticationSecurityOptions>().BindConfiguration("Authentication").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ForwardedHeadersOptionsConfig>().BindConfiguration("ForwardedHeaders");
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
}).AddEntityFrameworkStores<IdentityDbContext>().AddDefaultTokenProviders();
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
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
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
        options.AddEphemeralEncryptionKey();
        options.DisableAccessTokenEncryption();
        var aspNetCore = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableEndSessionEndpointPassthrough();
        if (builder.Environment.IsDevelopment()) aspNetCore.DisableTransportSecurityRequirement();
    });
builder.Services.AddSingleton<IAuthenticationRateLimiter, AuthenticationRateLimiter>();
builder.Services.AddSingleton<IClientIpAddressResolver, ClientIpAddressResolver>();
builder.Services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
builder.Services.AddHostedService<OpenIddictClientSeeder>();

var app = builder.Build();
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
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<MandatoryTwoFactorMiddleware>();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
