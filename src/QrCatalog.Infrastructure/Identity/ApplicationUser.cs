using Microsoft.AspNetCore.Identity;

namespace QrCatalog.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// İstifadəçinin aid olduğu müəssisə. <c>null</c> = platforma səviyyəli istifadəçi
    /// (super-admin) — onlar ad-siyahı ilə ayrıca yoxlanılır, rolla deyil.
    /// </summary>
    public Guid? CompanyId { get; set; }

    public string DisplayName { get; set; } = "";
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}
