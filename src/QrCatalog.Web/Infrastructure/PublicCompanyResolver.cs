using Microsoft.EntityFrameworkCore;
using QrCatalog.Infrastructure.Persistence;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Public kataloq səhifələri hansı müəssisəni göstərir? QR səhifəsinə lazım deyil
/// (token qlobaldır), amma /katalog hansısa müəssisəyə aid olmalıdır.
///
/// Hazırkı qayda: config-də <c>Public:DefaultCompanySlug</c> varsa, o; yoxdursa və sistemdə
/// TƏK müəssisə varsa, o. Birdən çox müəssisə + config yoxdursa → null (kataloq 404).
/// S2-01 (domen) qərarından sonra bura host-əsaslı həll gələcək:
/// müştəri domeni → Company.Domain uyğunluğu.
/// </summary>
public static class PublicCompanyResolver
{
    public static async Task<Guid?> ResolveAsync(
        AppDbContext db, IConfiguration config, CancellationToken ct = default)
    {
        var slug = config["Public:DefaultCompanySlug"];
        if (!string.IsNullOrWhiteSpace(slug))
        {
            return await db.Companies.AsNoTracking()
                .Where(c => c.Slug == slug && c.IsActive)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
        }

        var ids = await db.Companies.AsNoTracking()
            .Where(c => c.IsActive)
            .Select(c => c.Id)
            .Take(2)
            .ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }
}
