using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;
using PocketLedger.Web.Api;
using PocketLedger.Web.Authentication;
using PocketLedger.Web.Data;
using PocketLedger.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<WebDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("WebConnection")));
builder.Services.AddDataProtection().SetApplicationName("PocketLedger.Web").PersistKeysToDbContext<WebDbContext>();
builder.Services.AddSingleton<DatabaseTicketStore>();
builder.Services.AddSingleton<ISessionTicketReader>(services => services.GetRequiredService<DatabaseTicketStore>());
builder.Services.AddSingleton<SessionRefreshCoordinator>();
builder.Services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, ConfigureBffCookie>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = "BffCookie";
    options.DefaultChallengeScheme = "oidc";
}).AddCookie("BffCookie", options =>
{
    options.Cookie.Name = "PocketLedger.Web";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.LoginPath = "/Session/Login";
    options.AccessDeniedPath = "/Session/Login";
}).AddOpenIdConnect("oidc", options =>
{
    var authority = new Uri(builder.Configuration["Identity:Authority"] ?? "https://identity.localhost/");
    var backchannel = new Uri(builder.Configuration["Identity:BackchannelBaseUrl"] ?? authority.ToString());
    options.Authority = authority.ToString();
    options.MetadataAddress = builder.Configuration["Identity:MetadataAddress"];
    options.BackchannelHttpHandler = new IdentityBackchannelHandler(authority, backchannel);
    options.RequireHttpsMetadata = builder.Configuration.GetValue("Identity:RequireHttpsMetadata", true);
    options.ClientId = builder.Configuration["Identity:ClientId"] ?? "pocketledger-web";
    options.ClientSecret = builder.Configuration["Identity:ClientSecret"] ?? throw new InvalidOperationException("Identity:ClientSecret is required.");
    options.TokenValidationParameters = new TokenValidationParameters { IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(builder.Configuration["Authentication:SigningKey"] ?? throw new InvalidOperationException("Authentication:SigningKey is required."))) };
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.UsePkce = true;
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("offline_access");
    options.Scope.Add("pocketledger.api");
});
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IUserDateProvider, UserDateProvider>();
builder.Services.AddScoped<IUserContextService, WebUserContextService>();
builder.Services.AddTransient<AccessTokenHandler>();
builder.Services.AddHttpClient("IdentityToken", client => client.BaseAddress = new Uri(builder.Configuration["Identity:BackchannelBaseUrl"] ?? "https://identity.localhost/"));
AddApiClient<IAccountService, AccountsApiClient>();
AddApiClient<ICategoryService, CategoriesApiClient>();
AddApiClient<ITransactionService, TransactionsApiClient>();
AddApiClient<IRecurringTransactionService, RecurringTransactionsApiClient>();
AddApiClient<ICalendarService, CalendarApiClient>();
AddApiClient<IStatisticsService, StatisticsApiClient>();
AddApiClient<IImportExportService, ImportExportApiClient>();
AddApiClient<IDebtService, DebtsApiClient>();
AddApiClient<IPreferencesApiClient, PreferencesApiClient>();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WebDbContext>().Database.MigrateAsync();
}
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAll")) { forwarded.KnownIPNetworks.Clear(); forwarded.KnownProxies.Clear(); }
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? []) if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
app.UseForwardedHeaders(forwarded);
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Home/Error");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<BffSessionExpiredMiddleware>();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();
app.MapStaticAssets();
app.Run();

void AddApiClient<TService, TImplementation>() where TService : class where TImplementation : class, TService
{
    builder.Services.AddHttpClient<TService, TImplementation>(client => client.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "https://api.localhost/")).AddHttpMessageHandler<AccessTokenHandler>();
}

public partial class Program;
