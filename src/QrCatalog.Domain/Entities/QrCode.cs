using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

public enum QrTargetType
{
    /// <summary>Məhsul modeli — əsas istifadə (M3-dən sonra).</summary>
    Product = 0,

    /// <summary>Kateqoriya — plakat/vitrin kodları üçün.</summary>
    Category = 1,

    /// <summary>Arxiv səhifəsi — hədəfi istehsaldan çıxmış kodlar bura yönləndirilir.</summary>
    Archive = 2,
}

public enum QrCodeStatus
{
    Active = 0,
    Retired = 1,
}

/// <summary>
/// Çap olunmuş QR etiketin daimi qeydi. Token URL-ə düşür və QLOBAL unikaldır
/// (URL bütün müəssisələr üzrə ortaqdır); HumanCode etiketdə mətn kimi çap olunur
/// və müəssisə daxilində unikaldır. Kod HEÇ VAXT silinmir — çap olunmuş etiket
/// geri qaytarıla bilməz; yalnız retire olunur və ya yeni hədəfə yönləndirilir.
/// </summary>
public sealed class QrCode : ITenantOwned
{
    private QrCode() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }

    /// <summary>URL-dəki opak hissə: /q/{token}. Kriptoqrafik təsadüfi, qlobal unikal.</summary>
    public string Token { get; private set; } = null!;

    /// <summary>Etiketdəki insan-oxunan kod: "SZ-0142".</summary>
    public string HumanCode { get; private set; } = null!;

    /// <summary>HumanCode-un hissələri — növbəti nömrəni tapmaq üçün ayrıca saxlanılır.</summary>
    public string Prefix { get; private set; } = null!;
    public int Sequence { get; private set; }

    public QrTargetType TargetType { get; private set; }

    /// <summary>Hədəf entity-nin Id-si. Archive hədəfində null-dur.</summary>
    public Guid? TargetId { get; private set; }

    public QrCodeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static QrCode Create(
        Guid companyId, string token, string prefix, int sequence,
        QrTargetType targetType, Guid? targetId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token boş ola bilməz.", nameof(token));
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Prefiks boş ola bilməz.", nameof(prefix));
        if (sequence < 1)
            throw new ArgumentException("Nömrə 1-dən başlayır.", nameof(sequence));
        ValidateTarget(targetType, targetId);

        return new QrCode
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            Token = token,
            Prefix = prefix,
            Sequence = sequence,
            HumanCode = FormatHumanCode(prefix, sequence),
            TargetType = targetType,
            TargetId = targetId,
            Status = QrCodeStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public static string FormatHumanCode(string prefix, int sequence) => $"{prefix}-{sequence:D4}";

    /// <summary>Çap olunmuş etiketi xilas edən əməliyyat: köhnə kod yeni hədəfə baxır.</summary>
    public void Retarget(QrTargetType targetType, Guid? targetId)
    {
        ValidateTarget(targetType, targetId);
        TargetType = targetType;
        TargetId = targetId;
    }

    public void Retire() => Status = QrCodeStatus.Retired;
    public void Reactivate() => Status = QrCodeStatus.Active;

    private static void ValidateTarget(QrTargetType targetType, Guid? targetId)
    {
        if (targetType == QrTargetType.Archive)
        {
            if (targetId is not null)
                throw new ArgumentException("Arxiv hədəfinin TargetId-si olmur.", nameof(targetId));
        }
        else if (targetId is null || targetId == Guid.Empty)
        {
            throw new ArgumentException("Hədəf Id boş ola bilməz.", nameof(targetId));
        }
    }
}
