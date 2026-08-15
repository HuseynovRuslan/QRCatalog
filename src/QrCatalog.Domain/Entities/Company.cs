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

    /// <summary>Public səhifədəki "zəng et" düyməsi üçün. Boşdursa düymə görünmür.</summary>
    public string? Phone { get; private set; }

    /// <summary>wa.me linki üçün — yalnız rəqəmlər, ölkə kodu ilə (994501234567).</summary>
    public string? WhatsappNumber { get; private set; }

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

    public void UpdateProfile(string name, string? phone, string? whatsappNumber)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Müəssisə adı boş ola bilməz.", nameof(name));

        var whatsapp = string.IsNullOrWhiteSpace(whatsappNumber)
            ? null
            : new string(whatsappNumber.Where(char.IsAsciiDigit).ToArray());
        if (whatsapp is { Length: < 9 })
            throw new ArgumentException(
                "WhatsApp nömrəsi ölkə kodu ilə verilməlidir (məs. 994501234567).", nameof(whatsappNumber));

        Name = name.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        WhatsappNumber = whatsapp;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
