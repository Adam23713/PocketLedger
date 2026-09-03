using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using PocketLedger.Contracts;
using PocketLedger.Data;
using PocketLedger.Services;
using PocketLedger.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "OpenID Connect access token issued by PocketLedger Identity." };
        if (builder.Environment.IsDevelopment())
        {
            document.Components.SecuritySchemes["OAuth2"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri(builder.Configuration["Swagger:AuthorizationEndpoint"] ?? "http://localhost:5052/connect/authorize"),
                        TokenUrl = new Uri(builder.Configuration["Swagger:TokenEndpoint"] ?? "http://localhost:5052/connect/token"),
                        Scopes = new Dictionary<string, string> { ["openid"] = "OpenID identity", ["profile"] = "User profile", ["pocketledger.api"] = "PocketLedger API" }
                    }
                }
            };
        }
        return Task.CompletedTask;
    });
    options.AddOperationTransformer(async (operation, context, token) =>
    {
        if (context.Description.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any())
        {
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [] }];
            if (builder.Environment.IsDevelopment()) operation.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("OAuth2", context.Document)] = ["openid", "profile", "pocketledger.api"] });
        }

        var errorSchema = await context.GetOrCreateSchemaAsync(typeof(ApiError), null!, token);
        operation.Responses ??= new OpenApiResponses();
        foreach (var (status, description) in new[] { ("400", "Bad Request"), ("404", "Not Found"), ("500", "Internal Server Error") })
            operation.Responses.TryAdd(status, new OpenApiResponse { Description = description, Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = errorSchema } } });
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.Configure<UserDateOptions>(builder.Configuration.GetSection(UserDateOptions.SectionName));
builder.Services.AddSingleton<IUserDateProvider, UserDateProvider>();
builder.Services.AddDbContext<PocketLedgerDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("ApiConnection")));
builder.Services.AddRecurringTransactionProcessingDataAccess();
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
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "PocketLedger API v1");
        options.OAuthClientId(builder.Configuration["Swagger:ClientId"] ?? "pocketledger-swagger");
        options.OAuthScopes("openid", "profile", "pocketledger.api");
        options.OAuthUsePkce();
    });
}
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();
app.Run();

public partial class Program;
