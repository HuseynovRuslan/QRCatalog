using QrCatalog.Infrastructure.Identity;
using QrCatalog.Infrastructure.Tenancy;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Auth-dan SONRA işləyir: cookie-dəki <c>company_id</c> claim-ini oxuyub scoped tenant kontekstini
/// doldurur. Claim yoxdursa kontekst boş qalır — tenant-scoped sorğular fail-closed boş qayıdır.
/// </summary>
public sealed class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolutionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AmbientTenantContext tenant)
    {
        var claim = context.User.FindFirst(AppClaims.CompanyId)?.Value;
        if (Guid.TryParse(claim, out var companyId))
            tenant.Set(companyId);

        await _next(context);
    }
}
