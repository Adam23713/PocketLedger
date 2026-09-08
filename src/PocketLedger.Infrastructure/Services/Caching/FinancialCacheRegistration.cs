using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace PocketLedger.Services;

public static class FinancialCacheRegistration
{
    public static IServiceCollection AddFinancialCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FinancialCacheOptions>().Bind(configuration.GetSection("FinancialCache"))
            .Validate(options => options.LifetimeMinutes is >= 1 and <= 1440, "Financial cache lifetime must be between 1 and 1440 minutes.").ValidateOnStart();
        var connectionString = configuration["FinancialCache:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString)) return services;
        services.AddStackExchangeRedisCache(options =>
        {
            options.ConfigurationOptions = ConfigurationOptions.Parse(connectionString);
            options.ConfigurationOptions.AbortOnConnectFail = false;
            options.ConfigurationOptions.ConnectTimeout = 500;
            options.ConfigurationOptions.AsyncTimeout = 500;
            options.ConfigurationOptions.ConnectRetry = 0;
            options.ConfigurationOptions.BacklogPolicy = BacklogPolicy.FailFast;
        });
        services.AddScoped<FinancialCache>();
        return services;
    }
}
