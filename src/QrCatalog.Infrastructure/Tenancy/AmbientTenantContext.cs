using QrCatalog.Application.Abstractions;

namespace QrCatalog.Infrastructure.Tenancy;

/// <summary>
/// Scoped tenant konteksti. M1-də auth middleware girişdən sonra <see cref="Set"/> çağıracaq;
/// heç kim çağırmasa <see cref="CompanyId"/> null qalır və filtrlər fail-closed işləyir.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    public Guid? CompanyId { get; private set; }

    public void Set(Guid companyId) => CompanyId = companyId;
}
