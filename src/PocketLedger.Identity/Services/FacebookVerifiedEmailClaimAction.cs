using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;

namespace PocketLedger.Services;

public sealed class FacebookVerifiedEmailClaimAction() : ClaimAction(ClaimTypes.Email, ClaimValueTypes.String)
{
    public override void Run(JsonElement userData, ClaimsIdentity identity, string issuer)
    {
        if (!userData.TryGetProperty("email", out var emailElement) || emailElement.ValueKind != JsonValueKind.String) return;
        var email = emailElement.GetString()?.Trim();
        if (string.IsNullOrEmpty(email) || !new EmailAddressAttribute().IsValid(email)) return;

        // Meta exposes the account's primary email but no separate verification flag.
        identity.AddClaim(new Claim(ClaimTypes.Email, email, ClaimValueTypes.String, issuer));
        identity.AddClaim(new Claim("email_verified", bool.TrueString, ClaimValueTypes.Boolean, issuer));
    }
}
