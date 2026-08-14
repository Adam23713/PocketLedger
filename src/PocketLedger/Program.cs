using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger;
using PocketLedger.Configuration;
using PocketLedger.Data;
using PocketLedger.Middleware;
using PocketLedger.Models.Entities;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

LoadDevelopmentEnvironmentFile();
var builder = WebApplication.CreateBuilder(args);

if (Environment.GetEnvironmentVariable("POCKETLEDGER_MAXIMUM_USER_COUNT") is { } maximumUsers) builder.Configuration["AccountManagement:MaximumUserCount"] = maximumUsers;

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddOptions<AccountManagementOptions>().BindConfiguration("AccountManagement").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<AuthenticationSecurityOptions>().BindConfiguration("Authentication").ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<ForwardedHeadersOptionsConfig>().BindConfiguration("ForwardedHeaders");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddDbContext<PocketLedgerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
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
    options.SignIn.RequireConfirmedAccount = false;
}).AddEntityFrameworkStores<PocketLedgerDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddSingleton<IAuthenticationRateLimiter, AuthenticationRateLimiter>();
builder.Services.AddScoped<IAuthenticationAuditService, AuthenticationAuditService>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<ITransactionService, TransactionService>();
builder.Services.AddScoped<IRecurringTransactionService, RecurringTransactionService>();
builder.Services.AddScoped<ICalendarService, CalendarService>();
builder.Services.AddScoped<IStatisticsService, StatisticsService>();
builder.Services.AddScoped<IImportExportService, ImportExportService>();
builder.Services.AddScoped<IDebtService, DebtService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<RecurringTransactionWorker>();

var app = builder.Build();

if (args.Length > 0 && (args[0] == "bootstrap-identity" || args[0] == "account"))
{
    var exitCode = await CommandRunner.RunAsync(args, app.Services);
    Environment.ExitCode = exitCode;
    return;
}

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PocketLedgerDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    if (builder.Configuration.GetValue("HttpsRedirection:Enabled", true))
    {
        app.UseHttpsRedirection();
    }
}

var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
{
    if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
}
app.UseForwardedHeaders(forwarded);
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<MandatoryTwoFactorMiddleware>();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();

static void LoadDevelopmentEnvironmentFile()
{
    var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    if (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase)) return;
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    string? path = null;
    while (directory is not null)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate)) { path = candidate; break; }
        directory = directory.Parent;
    }
    if (path is null) return;
    foreach (var line in File.ReadLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
        var separator = trimmed.IndexOf('=');
        if (separator <= 0) continue;
        var key = trimmed[..separator].Trim();
        if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, trimmed[(separator + 1)..]);
    }
}

public partial class Program;
