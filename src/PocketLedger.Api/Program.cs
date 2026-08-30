using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PocketLedger.Data;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddDbContext<PocketLedgerDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("ApiConnection")));
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
var signingKey = builder.Configuration["Authentication:SigningKey"] ?? throw new InvalidOperationException("Authentication:SigningKey is required.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Authentication:Issuer"] ?? "https://identity.localhost/",
        ValidateAudience = true,
        ValidAudience = "pocketledger-api",
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
        NameClaimType = ClaimTypes.NameIdentifier
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<PocketLedgerDbContext>().Database.MigrateAsync();
}
var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
if (builder.Configuration.GetValue<bool>("ForwardedHeaders:TrustAll")) { forwarded.KnownIPNetworks.Clear(); forwarded.KnownProxies.Clear(); }
foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? []) if (IPAddress.TryParse(proxy, out var address)) forwarded.KnownProxies.Add(address);
app.UseForwardedHeaders(forwarded);
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi("/openapi/{documentName}.json");
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.Run();

public partial class Program;
