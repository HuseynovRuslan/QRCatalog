namespace QrCatalog.Domain.Common;

/// <summary>
/// Tenant-scoped entity-lərin hamısı bunu implement edir. AppDbContext bu interfeysi
/// daşıyan hər entity-yə avtomatik fail-closed query filtri tətbiq edir:
/// cari şirkət məlum deyilsə, sorğu HEÇ NƏ qaytarmır — default şirkət verilmir.
/// </summary>
public interface ITenantOwned
{
    Guid CompanyId { get; }
}
