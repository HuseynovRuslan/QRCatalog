using System.Security.Claims;
using QrCatalog.Application.Abstractions;

namespace QrCatalog.Web.Infrastructure;

/// <summary>Cari HTTP sorğusunun istifadəçisi — claims-dən oxunur, scoped.</summary>
public sealed class CurrentUserAccessor : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserAccessor(IHttpContextAccessor httpContextAccessor) =>
        _httpContextAccessor = httpContextAccessor;

    public string? Email =>
        _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)
        ?? _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
}
