using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace QrCatalog.Infrastructure.Identity;

/// <summary>
/// Girişdə cookie-yə <c>company_id</c> claim-i əlavə edir — tenant middleware bunu oxuyub
/// AmbientTenantContext-i doldurur. Claim yoxdursa kontekst boş qalır və filtrlər fail-closed işləyir.
/// </summary>
public sealed class AppClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser, ApplicationRole>
{
    public AppClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (user.CompanyId is { } companyId)
            identity.AddClaim(new Claim(AppClaims.CompanyId, companyId.ToString()));

        identity.AddClaim(new Claim(AppClaims.DisplayName, user.DisplayName));
        return identity;
    }
}
