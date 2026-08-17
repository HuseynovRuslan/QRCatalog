using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

/// <summary>Obyektin növü — xəritədə rəng, siyahıda süzgəc üçün.</summary>
public enum SiteKind
{
    Other = 0,
    Park = 1,
    Hotel = 2,
    Cafe = 3,
    School = 4,
    Residential = 5,
    Beach = 6,
}

/// <summary>
/// Quraşdırılma obyekti — məhsulların fiziki olarak durduğu yer: park, hotel, kafe terrası.
/// "Bu skamyalar hansı parkdadır?" sualının cavabı buradadır; satışdan sonra xidmət,
/// zəmanət və təkrar sifariş danışığı məhz bu siyahıya söykənir.
///
/// Tenant-scoped: filtri AppDbContext avtomatik qoşur.
/// </summary>
public sealed class Site : ITenantOwned
{
    private Site() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }

    public string Name { get; private set; } = null!;
    public SiteKind Kind { get; private set; }
    public string? Address { get; private set; }

    /// <summary>Xəritə mövqeyi. Dərəcə ilə: enlik [-90, 90], uzunluq [-180, 180].</summary>
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    /// <summary>Əlaqədar şəxs — obyektdə kimlə danışılır (rəis, menecer).</summary>
    public string? ContactName { get; private set; }
    public string? ContactPhone { get; private set; }

    /// <summary>Daxili qeyd — müştəri görmür.</summary>
    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Site Create(
        Guid companyId, string name, SiteKind kind, double latitude, double longitude,
        string? address = null, string? contactName = null, string? contactPhone = null,
        string? note = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));

        var now = DateTime.UtcNow;
        var site = new Site
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        site.Update(name, kind, latitude, longitude, address, contactName, contactPhone, note);
        return site;
    }

    public void Update(
        string name, SiteKind kind, double latitude, double longitude,
        string? address, string? contactName, string? contactPhone, string? note)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Obyektin adı boş ola bilməz.", nameof(name));

        // Koordinat səhvi xəritədə "obyekt okeanın ortasında" kimi görünür — girişdə tutulur.
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new ArgumentException("Enlik -90 ilə 90 arasında olmalıdır.", nameof(latitude));
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new ArgumentException("Uzunluq -180 ilə 180 arasında olmalıdır.", nameof(longitude));

        Name = name.Trim();
        Kind = kind;
        Latitude = latitude;
        Longitude = longitude;
        Address = Clean(address);
        ContactName = Clean(contactName);
        ContactPhone = Clean(contactPhone);
        Note = Clean(note);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// QEYD: əvvəl burada `SiteItem` var idi — obyektdə modelin SAYINI saxlayırdı
// ("bu parkda 24 skamya"). O, <see cref="SiteUnit"/> ilə əvəz olundu: hər fiziki nüsxə
// öz qeydi və öz koordinatı ilə. Say indi vahidlərdən HESABLANIR — eyni faktı iki yerdə
// saxlamaq onların bir gün ayrılması ilə bitir.
