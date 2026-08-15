using QrCatalog.Application.Abstractions;

namespace QrCatalog.Infrastructure.Tenancy;

/// <summary>
/// Tenant-sız kontekst — migration və design-time kimi sistem əməliyyatları üçün.
/// CompanyId həmişə null-dur, yəni tenant-scoped sorğular fail-closed boş qayıdır.
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    private NullTenantContext() { }

    public Guid? CompanyId => null;
}
