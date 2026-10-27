using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace PocketLedger.Landing.Contact;

// A signed "form rendered at" stamp: bots that post instantly, or replay one rendered form for hours, are turned away.
public sealed class ContactFormTimestamp(IDataProtectionProvider provider, IOptions<ContactOptions> options, TimeProvider timeProvider)
{
    private readonly ITimeLimitedDataProtector protector = provider.CreateProtector("PocketLedger.Landing.ContactForm.v1").ToTimeLimitedDataProtector();

    public string Issue()
    {
        var now = timeProvider.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(options.Value.Limits.MaximumFormAgeMinutes);
        return protector.Protect(now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), now.Add(lifetime));
    }

    public bool IsAcceptable(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512) return false;
        try
        {
            var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(protector.Unprotect(token, out _), CultureInfo.InvariantCulture));
            return timeProvider.GetUtcNow() - issuedAt >= TimeSpan.FromSeconds(options.Value.Limits.MinimumFillSeconds);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or OverflowException)
        {
            return false;
        }
    }
}
