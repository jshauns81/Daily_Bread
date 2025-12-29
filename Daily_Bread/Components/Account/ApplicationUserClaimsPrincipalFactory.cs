using System.Security.Claims;
using Daily_Bread.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Daily_Bread.Components.Account;

/// <summary>
/// Custom claims principal factory that adds HouseholdId claim on login.
/// </summary>
public class ApplicationUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
{
    public ApplicationUserClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Add HouseholdId claim if user belongs to a household
        if (user.HouseholdId.HasValue)
        {
            identity.AddClaim(new Claim("HouseholdId", user.HouseholdId.Value.ToString()));
        }

        return identity;
    }
}
