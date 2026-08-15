using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

/// <summary>
/// Bir QR skanı. Yüksək həcmli cədvəldir — Id long identity-dir, IP və digər PII SAXLANILMIR:
/// yalnız cihaz növü və dil. Yazı asinxron gedir (ScanEventWriter), oxu hesabatlardadır.
/// </summary>
public sealed class ScanEvent : ITenantOwned
{
    private ScanEvent() { } // EF Core

    public long Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid QrCodeId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>mobile · tablet · desktop · unknown</summary>
    public string DeviceKind { get; private set; } = null!;

    /// <summary>Accept-Language-in ilk teqi: "az", "ru-RU"…</summary>
    public string? Lang { get; private set; }

    public static ScanEvent Create(Guid companyId, Guid qrCodeId, string deviceKind, string? lang)
    {
        return new ScanEvent
        {
            CompanyId = companyId,
            QrCodeId = qrCodeId,
            OccurredAtUtc = DateTime.UtcNow,
            DeviceKind = deviceKind,
            Lang = lang,
        };
    }
}
