using System.Text.RegularExpressions;
using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

/// <summary>
/// Kateqoriya ağacı — şezlonq, skameyka, çətir və s. Tenant-scoped:
/// filtri AppDbContext avtomatik qoşur, kod burada CompanyId yoxlamır.
/// </summary>
public sealed partial class Category : ITenantOwned
{
    private Category() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>Public URL üçün: /katalog/{slug}. Müəssisə daxilində unikal.</summary>
    public string Slug { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>
    /// QR insan-oxunan kodlarının prefiksi (SZ-0142-dəki "SZ"). M4-də sayğacla birləşir.
    /// Boş ola bilər — o halda bu kateqoriyanın məhsullarına ümumi prefiks düşür.
    /// </summary>
    public string? CodePrefix { get; private set; }

    /// <summary>Eyni valideynin altındaki sıra.</summary>
    public int SortOrder { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static Category Create(
        Guid companyId, string name, string slug, Guid? parentId, int sortOrder,
        string? description = null, string? codePrefix = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));

        var category = new Category
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ParentId = parentId,
            SortOrder = sortOrder,
            CreatedAtUtc = DateTime.UtcNow,
        };
        category.Update(name, description, codePrefix);
        category.SetSlug(slug);
        return category;
    }

    public void Update(string name, string? description, string? codePrefix)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Kateqoriya adı boş ola bilməz.", nameof(name));
        if (codePrefix is not null && !CodePrefixPattern().IsMatch(codePrefix))
            throw new ArgumentException(
                "Kod prefiksi 2-4 böyük latın hərfi olmalıdır (məs. SZ, SKM).", nameof(codePrefix));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CodePrefix = codePrefix;
    }

    public void SetSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug boş ola bilməz.", nameof(slug));
        Slug = slug;
    }

    public void MoveTo(Guid? parentId, int sortOrder)
    {
        if (parentId == Id)
            throw new ArgumentException("Kateqoriya öz-özünün valideyni ola bilməz.");
        ParentId = parentId;
        SortOrder = sortOrder;
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    [GeneratedRegex("^[A-Z]{2,4}$")]
    private static partial Regex CodePrefixPattern();
}
