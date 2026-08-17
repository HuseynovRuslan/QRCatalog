using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

/// <summary>
/// Quraşdırılma obyektləri — məhsulların fiziki durduğu yerlər və xəritə nöqtələri.
/// Obyektdəki SAY ayrıca saxlanılmır, <see cref="SiteUnit"/> qeydlərindən hesablanır:
/// eyni faktın iki mənbəyi bir gün mütləq ayrılır.
///
/// Obyekt SİLİNƏ BİLƏR (məhsuldan fərqli): səhv ünvan tarixi məlumat deyil. Nüsxələr
/// silinmir — obyekt gedəndə onlar anbara qayıdır (FK SetNull).
/// </summary>
public static class SiteEndpoints
{
    private const string DefaultLang = "az";

    public static void MapSiteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/sites");

        group.MapGet("/", async (AppDbContext db, string? search, Guid? productId) =>
        {
            var query = db.Sites.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s =>
                    EF.Functions.ILike(s.Name, $"%{search}%") ||
                    (s.Address != null && EF.Functions.ILike(s.Address, $"%{search}%")));
            // "Bu model harada quraşdırılıb" — məhsul səhifəsindəki xəritə bunu işlədir
            if (productId is { } product)
                query = query.Where(s => db.SiteUnits.Any(u =>
                    u.SiteId == s.Id && u.ProductId == product && u.Status != UnitStatus.Removed));

            var sites = await query
                .OrderBy(s => s.Name)
                .Select(s => new
                {
                    s.Id, s.Name, Kind = s.Kind.ToString(), s.Address,
                    s.Latitude, s.Longitude, s.ContactName, s.ContactPhone, s.Note,
                    s.UpdatedAtUtc,
                })
                .ToListAsync();

            var siteIds = sites.Select(s => s.Id).ToList();

            // Model üzrə yığım — çıxarılmış nüsxələr sayılmır
            var grouped = await db.SiteUnits.AsNoTracking()
                .Where(u => u.SiteId != null && siteIds.Contains(u.SiteId!.Value) &&
                            u.Status != UnitStatus.Removed)
                .GroupBy(u => new { SiteId = u.SiteId!.Value, u.ProductId })
                .Select(g => new { g.Key.SiteId, g.Key.ProductId, Count = g.Count() })
                .ToListAsync();

            var names = await ProductNamesAsync(db, grouped.Select(g => g.ProductId));

            var items = sites.Select(s =>
            {
                var lines = grouped
                    .Where(g => g.SiteId == s.Id)
                    .OrderByDescending(g => g.Count)
                    .Select(g => new SiteItemDto(
                        g.ProductId,
                        names.GetValueOrDefault(g.ProductId, "(silinmiş məhsul)"),
                        g.Count))
                    .ToList();

                return new SiteDto(
                    s.Id, s.Name, s.Kind, s.Address, s.Latitude, s.Longitude,
                    s.ContactName, s.ContactPhone, s.Note, s.UpdatedAtUtc,
                    lines, lines.Sum(l => l.Quantity));
            }).ToList();

            return Results.Ok(items);
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPost("/", async (SaveSiteRequest request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            Site site;
            try
            {
                site = Site.Create(companyId, request.Name, ParseKind(request.Kind),
                    request.Latitude, request.Longitude, request.Address,
                    request.ContactName, request.ContactPhone, request.Note);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            db.Sites.Add(site);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/sites/{site.Id}", new { site.Id });
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}", async (Guid id, SaveSiteRequest request, AppDbContext db) =>
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id);
            if (site is null) return Results.NotFound();

            try
            {
                site.Update(request.Name, ParseKind(request.Kind),
                    request.Latitude, request.Longitude, request.Address,
                    request.ContactName, request.ContactPhone, request.Note);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id);
            if (site is null) return Results.NotFound();

            // Nüsxələr silinmir — anbara qayıdır. Fiziki skamya obyekt qeydi
            // silinəndə yoxa çıxmır, ona görə tarixçəni saxlayırıq.
            var units = await db.SiteUnits.Where(u => u.SiteId == id).ToListAsync();
            foreach (var unit in units)
                unit.Place(null, null, null, unit.InstalledOn);

            db.Sites.Remove(site);
            await db.SaveChangesAsync();
            return Results.Ok(new { returnedToStock = units.Count });
        })
        .RequireAuthorization(Policies.CanManage)
        .RequireAntiforgery();
    }

    internal static async Task<Dictionary<Guid, string>> ProductNamesAsync(
        AppDbContext db, IEnumerable<Guid> productIds)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                Name = p.Translations.Where(t => t.Lang == DefaultLang)
                    .Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
            })
            .ToDictionaryAsync(p => p.Id, p => p.Name);
    }

    private static SiteKind ParseKind(string? kind) =>
        Enum.TryParse<SiteKind>(kind, ignoreCase: true, out var parsed) ? parsed : SiteKind.Other;
}

public sealed record SaveSiteRequest(
    string Name, string? Kind, double Latitude, double Longitude,
    string? Address, string? ContactName, string? ContactPhone, string? Note);

/// <summary>Obyektdə bir modelin yığımı — nüsxə qeydlərindən hesablanır.</summary>
public sealed record SiteItemDto(Guid ProductId, string ProductName, int Quantity);

public sealed record SiteDto(
    Guid Id, string Name, string Kind, string? Address, double Latitude, double Longitude,
    string? ContactName, string? ContactPhone, string? Note, DateTime UpdatedAtUtc,
    List<SiteItemDto> Items, int TotalQuantity);
