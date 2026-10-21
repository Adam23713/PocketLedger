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
    // Each Argon2id derivation of a data key holds 64 MiB of memory.
    [Range(1, 16)] public int DataKeyDerivationConcurrency { get; set; } = 2;
    [Range(1, 60)] public int DataKeyDerivationWaitSeconds { get; set; } = 10;
}

public sealed class PublicRegistrationOptions
{
    public const string SectionName = "Registration";
    public const int MaximumAllowedUserCount = 100_000;

    // Defaults used until an administrator saves the registration settings on the admin dashboard.
    public bool Enabled { get; set; }
    [Range(1, MaximumAllowedUserCount)] public int? MaximumUserCount { get; set; }
}

public sealed class ForwardedHeadersOptionsConfig
{
    public string[] KnownProxies { get; set; } = [];
}
