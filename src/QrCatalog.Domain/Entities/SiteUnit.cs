using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

public enum UnitStatus
{
    /// <summary>Obyektdə quraşdırılıb və işləyir.</summary>
    Installed = 0,

    /// <summary>Anbarda — hələ quraşdırılmayıb (obyekt boş ola bilər).</summary>
    InStock = 1,

    /// <summary>Təmirdədir, yerindən götürülüb.</summary>
    InRepair = 2,

    /// <summary>Sıradan çıxıb / çıxarılıb. Qeyd SİLİNMİR — tarixçə qalır.</summary>
    Removed = 3,
}

/// <summary>
/// Bir FİZİKİ nüsxə: konkret skamya, konkret şezlonq. Model səviyyəsindəki
/// "bu parkda 24 skamya var" cavabı xidmət üçün yetərli deyil — zəng gələndə
/// "hansı skamya" sualına cavab lazımdır: hansı nüsxə, harada durur, nə vaxt
/// quraşdırılıb, zəmanəti nə vaxt bitir.
///
/// Öz koordinatı var: parkın içində skamyalar bir-birindən 50 metr aralı olur, obyektin
/// tək nöqtəsi onları göstərmir. Koordinat verilməyibsə xəritədə obyektin nöqtəsi işlədilir.
///
/// Nüsxə SİLİNMİR, statusu dəyişir — çıxarılmış avadanlığın tarixçəsi zəmanət
/// mübahisəsində lazım olur.
/// </summary>
public sealed class SiteUnit : ITenantOwned
{
    private SiteUnit() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }

    /// <summary>Hansı model.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Hansı obyektdə. Null = anbarda (status InStock).</summary>
    public Guid? SiteId { get; private set; }

    /// <summary>İnsan-oxunan nüsxə kodu: "SK-PR-3N/007". Müəssisə daxilində unikal.</summary>
    public string Code { get; private set; } = null!;

    /// <summary>Dəqiq mövqe. Null olduqda xəritə obyektin koordinatını işlədir.</summary>
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }

    public UnitStatus Status { get; private set; }
    public DateOnly? InstalledOn { get; private set; }
    public string? Note { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static SiteUnit Create(
        Guid companyId, Guid productId, Guid? siteId, string code,
        double? latitude = null, double? longitude = null,
        DateOnly? installedOn = null, string? note = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));
        if (productId == Guid.Empty)
            throw new ArgumentException("Model seçilməlidir.", nameof(productId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Nüsxə kodu boş ola bilməz.", nameof(code));

        var now = DateTime.UtcNow;
        var unit = new SiteUnit
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProductId = productId,
            Code = code.Trim().ToUpperInvariant(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        unit.Place(siteId, latitude, longitude, installedOn);
        unit.SetNote(note);
        return unit;
    }

    /// <summary>Obyektə yerləşdirir və ya anbara qaytarır. Status buna görə dəyişir.</summary>
    public void Place(Guid? siteId, double? latitude, double? longitude, DateOnly? installedOn)
    {
        ValidateCoordinates(latitude, longitude);

        SiteId = siteId;
        Latitude = latitude;
        Longitude = longitude;
        InstalledOn = installedOn;
        // Obyektə bağlıdırsa quraşdırılmış sayılır; obyekt yoxdursa anbardadır.
        // Təmir/çıxarılma halları SetStatus ilə açıq şəkildə verilir.
        if (Status is UnitStatus.Installed or UnitStatus.InStock)
            Status = siteId is null ? UnitStatus.InStock : UnitStatus.Installed;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void MoveTo(double latitude, double longitude)
    {
        ValidateCoordinates(latitude, longitude);
        Latitude = latitude;
        Longitude = longitude;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetStatus(UnitStatus status)
    {
        Status = status;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetNote(string? note)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidateCoordinates(double? latitude, double? longitude)
    {
        if (latitude is null && longitude is null)
            return;
        if (latitude is null || longitude is null)
            throw new ArgumentException("Enlik və uzunluq birlikdə verilməlidir.", nameof(latitude));
        if (double.IsNaN(latitude.Value) || latitude is < -90 or > 90)
            throw new ArgumentException("Enlik -90 ilə 90 arasında olmalıdır.", nameof(latitude));
        if (double.IsNaN(longitude.Value) || longitude is < -180 or > 180)
            throw new ArgumentException("Uzunluq -180 ilə 180 arasında olmalıdır.", nameof(longitude));
    }
}
