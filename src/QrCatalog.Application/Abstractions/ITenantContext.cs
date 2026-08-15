namespace QrCatalog.Application.Abstractions;

/// <summary>
/// Cari sorğunun hansı müəssisəyə aid olduğunu bildirir.
/// <c>null</c> = şirkət təyin olunmayıb → tenant-scoped sorğular boş nəticə qaytarır (fail-closed).
/// M1-də auth middleware bunu dolduracaq.
/// </summary>
public interface ITenantContext
{
    Guid? CompanyId { get; }
}
