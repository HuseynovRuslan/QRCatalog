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

    /// <summary>
    /// Tək sahəli «işçi kodu» girişi üçün: kodun SHA-256 hash-i. Kodun ÖZÜ heç yerdə
    /// saxlanılmır — yalnız yaradıldığı anda bir dəfə göstərilir.
    ///
    /// Niyə hash, niyə determinist: kod həm şəxsiyyət, həm parol rolunu oynayır, ona
    /// görə giriş zamanı ona görə istifadəçi TAPILMALIDIR. Identity-nin parol hash-i
    /// hər dəfə fərqli salt işlədir, onunla axtarış mümkün deyil. Kod insan seçmir —
    /// 10 simvol təsadüfi (~50 bit), lüğət hücumu mövzusu deyil, ona görə saltsız
    /// SHA-256 burada yerindədir. Dəqiqədə 10 cəhd limiti kobud gücü kəsir.
    ///
    /// <c>null</c> = bu istifadəçidə kod yoxdur (yalnız e-poçt+parol ilə girir).
    /// </summary>
    public string? AccessCodeHash { get; set; }
}

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }
    public ApplicationRole(string name) : base(name) { }
}
