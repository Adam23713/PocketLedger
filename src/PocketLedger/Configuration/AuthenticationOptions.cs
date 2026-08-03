using System.ComponentModel.DataAnnotations;

namespace PocketLedger.Configuration;

public sealed class AccountManagementOptions
{
    [Range(1, 100)] public int MaximumUserCount { get; set; } = 1;
}

public sealed class AuthenticationSecurityOptions
{
    [Range(1, 20)] public int RateLimitPermitCount { get; set; } = 3;
    [Range(1, 3600)] public int RateLimitWindowSeconds { get; set; } = 60;
}

public sealed class ForwardedHeadersOptionsConfig
{
    public string[] KnownProxies { get; set; } = [];
}
