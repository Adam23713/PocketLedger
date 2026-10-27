using MailKit.Security;

namespace PocketLedger.Landing.Contact;

public sealed class ContactOptions
{
    public const string SectionName = "Contact";

    public string RecipientAddress { get; set; } = "info@pocketledger.dev";

    // Set only when a trusted reverse proxy overwrites this header with the real client address; otherwise the socket peer is used.
    public string? ClientIpHeader { get; set; }

    public ContactSmtpOptions Smtp { get; set; } = new();
    public ContactTurnstileOptions Turnstile { get; set; } = new();
    public ContactLimitOptions Limits { get; set; } = new();
}

public sealed class ContactSmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "PocketLedger contact form";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

public sealed class ContactTurnstileOptions
{
    public string? SiteKey { get; set; }
    public string? SecretKey { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SiteKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

public sealed class ContactLimitOptions
{
    public int PerClientPerHour { get; set; } = 3;
    public int PerSenderPerDay { get; set; } = 3;
    public int GlobalPerHour { get; set; } = 20;
    public int MinimumFillSeconds { get; set; } = 3;
    public int MaximumFormAgeMinutes { get; set; } = 120;
}
