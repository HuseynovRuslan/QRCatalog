using System.Text.RegularExpressions;

namespace QrCatalog.Domain.Entities;

/// <summary>
/// Tenant — sistemdən istifadə edən müəssisə. Bütün tenant-scoped cədvəllər
/// <see cref="Common.ITenantOwned"/> vasitəsilə buna bağlanır.
/// </summary>
public sealed partial class Company
{
    private Company() { } // EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;

    /// <summary>URL-də və kod prefikslərində istifadə olunan qısa ad: yalnız a-z, 0-9, defis.</summary>
    public string Slug { get; private set; } = null!;

    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Company Create(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Müəssisə adı boş ola bilməz.", nameof(name));
        if (!SlugPattern().IsMatch(slug))
            throw new ArgumentException(
                "Slug yalnız kiçik latın hərfləri, rəqəm və defisdən ibarət olmalıdır.", nameof(slug));

        return new Company
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = slug,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public void Deactivate() => IsActive = false;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
