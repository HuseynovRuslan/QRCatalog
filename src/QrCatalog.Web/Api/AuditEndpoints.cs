using Microsoft.EntityFrameworkCore;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        // Yalnız Admin (CanManage) — jurnal həssas məlumatdır
        app.MapGet("/api/admin/audit", async (AppDbContext db, int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.AuditEntries.AsNoTracking();
            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(a => a.OccurredAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditDto(
                    a.OccurredAtUtc, a.UserEmail, a.EntityType, a.EntityId, a.Action, a.Changes))
                .ToListAsync();

            return Results.Ok(new PagedResult<AuditDto>(items, total, page, pageSize));
        })
        .RequireAuthorization(Policies.CanManage);
    }
}

public sealed record AuditDto(
    DateTime OccurredAtUtc, string UserEmail, string EntityType, string EntityId,
    string Action, string? Changes);
