using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PocketLedger.Models.Entities;
using PocketLedger.Security;

namespace PocketLedger.Services;

public sealed class ApplicationUserClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.IsBootstrapAdministrator) identity.AddClaim(new Claim(BootstrapAdministratorAuthorization.ClaimType, bool.TrueString));
        return identity;
    }
}
