using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AspNet.Security.OAuth.Apple;

namespace PocketLedger.Services;

public static class AppleIdentityClaims
{
    public static void Normalize(ClaimsIdentity identity)
    {
        var verifiedClaims = identity.FindAll("email_verified").ToArray();
        foreach (var claim in verifiedClaims) identity.RemoveClaim(claim);

        var emailClaims = identity.FindAll(ClaimTypes.Email).ToArray();
        foreach (var claim in emailClaims) identity.RemoveClaim(claim);

        var email = emailClaims.Length == 1 ? emailClaims[0].Value.Trim() : string.Empty;
        var verified = verifiedClaims.Length == 1 && bool.TryParse(verifiedClaims[0].Value, out var value) && value;
        if (verified && !string.IsNullOrEmpty(email) && new EmailAddressAttribute().IsValid(email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email, ClaimValueTypes.String, AppleAuthenticationDefaults.Issuer));
            identity.AddClaim(new Claim("email_verified", bool.TrueString, ClaimValueTypes.Boolean, AppleAuthenticationDefaults.Issuer));
        }
    }
}
