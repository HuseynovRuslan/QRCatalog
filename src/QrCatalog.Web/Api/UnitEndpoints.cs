using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

/// <summary>
/// Fiziki nüsxələr: konkret skamya, konkret şezlonq. "Bu parkda 24 skamya var" cavabı
/// xidmət zəngində yetməz — hansı nüsxə, harada durur, nə vaxt quraşdırılıb lazımdır.
///
/// Nüsxə SİLİNMİR, statusu dəyişir (təmir, çıxarılıb): zəmanət mübahisəsində tarixçə
/// dəlildir. Səhvən yaradılmış qeydi silmək üçün DELETE var, amma o CanManage tələb edir.
/// </summary>
public static class UnitEndpoints
{
    private const int MaxCreateRetries = 5;
    private const int MaxBulkQuantity = 500;

    /// <summary>Qızıl bucaq — nüsxələri obyektin ətrafına təbii səpələmək üçün.</summary>
    private const double GoldenAngle = 2.399963229728653;

    public static void MapUnitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/units");

        group.MapGet("/", async (AppDbContext db, Guid? productId, Guid? siteId,
            string? status, string? search) =>
        {
            var query = db.SiteUnits.AsNoTracking();
            if (productId is { } product) query = query.Where(u => u.ProductId == product);
            if (siteId is { } site) query = query.Where(u => u.SiteId == site);
            if (Enum.TryParse<UnitStatus>(status, ignoreCase: true, out var parsed))
                query = query.Where(u => u.Status == parsed);
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(u => EF.Functions.ILike(u.Code, $"%{search}%"));

            var units = await query
                .OrderBy(u => u.Code)
                .Select(u => new
                {
                    u.Id, u.Code, u.ProductId, u.SiteId, u.Latitude, u.Longitude,
                    Status = u.Status.ToString(), u.InstalledOn, u.Note, u.UpdatedAtUtc,
                })
                .ToListAsync();

            var names = await SiteEndpoints.ProductNamesAsync(db, units.Select(u => u.ProductId));
            var sites = await db.Sites.AsNoTracking()
                .Select(s => new { s.Id, s.Name, s.Latitude, s.Longitude })
                .ToDictionaryAsync(s => s.Id);

            var items = units.Select(u =>
            {
                var host = u.SiteId is { } id ? sites.GetValueOrDefault(id) : null;
                // Nüsxənin öz koordinatı yoxdursa obyektin nöqtəsi işlədilir — anbardaki
                // nüsxənin ümumiyyətlə mövqeyi olmur, xəritədə göstərilmir.
                return new UnitDto(
                    u.Id, u.Code, u.ProductId,
                    names.GetValueOrDefault(u.ProductId, "(silinmiş məhsul)"),
                    u.SiteId, host?.Name,
                    u.Latitude ?? host?.Latitude,
                    u.Longitude ?? host?.Longitude,
                    u.Latitude is not null,
                    u.Status, u.InstalledOn, u.Note, u.UpdatedAtUtc);
            }).ToList();

            return Results.Ok(items);
        })
        .RequireAuthorization(Policies.CanView);

        // Toplu yaratma: "bu parka 24 skamya qoyduq" — 24 dəfə forma doldurmaq
        // real işdə heç kimin etmədiyi bir şeydir
        group.MapPost("/bulk", async (
            BulkCreateUnitsRequest request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");
            if (request.Quantity is < 1 or > MaxBulkQuantity)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: $"Ədəd 1 ilə {MaxBulkQuantity} arasında olmalıdır.");

            var product = await db.Products.AsNoTracking()
                .Where(p => p.Id == request.ProductId)
                .Select(p => new { p.Id, p.Sku, p.CategoryId })
                .FirstOrDefaultAsync();
            if (product is null)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Məhsul tapılmadı.");

            Site? site = null;
            if (request.SiteId is { } siteId)
            {
                site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == siteId);
                if (site is null)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Obyekt tapılmadı.");
            }

            // Kod prefiksi: SKU varsa o, yoxsa kateqoriya prefiksi
            var prefix = product.Sku;
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = await db.Categories.AsNoTracking()
                    .Where(c => c.Id == product.CategoryId)
                    .Select(c => c.CodePrefix)
                    .FirstOrDefaultAsync() ?? "N";

            var created = new List<string>();
            for (var attempt = 1; ; attempt++)
            {
                // Növbəti nömrə: bu modelin mövcud nüsxə sayı. Unikal indeks + təkrar cəhd
                // yarışı həll edir; nüsxə yaratmaq nadir əməliyyatdır, kilid lazım deyil.
                var existing = await db.SiteUnits.CountAsync(u => u.ProductId == product.Id);
                var units = new List<SiteUnit>();

                for (var i = 0; i < request.Quantity; i++)
                {
                    var (lat, lng) = site is null
                        ? (default(double?), default(double?))
                        : Scatter(site.Latitude, site.Longitude, existing + i, request.SpreadMeters);

                    units.Add(SiteUnit.Create(
                        companyId, product.Id, request.SiteId,
                        $"{prefix}/{existing + i + 1:D3}",
                        lat, lng,
                        request.InstalledOn, request.Note));
                }

                db.SiteUnits.AddRange(units);
                try
                {
                    await db.SaveChangesAsync();
                    created = units.Select(u => u.Code).ToList();
                    break;
                }
                catch (DbUpdateException) when (attempt < MaxCreateRetries)
                {
                    foreach (var unit in units)
                        db.Entry(unit).State = EntityState.Detached;
                }
            }

            return Results.Created("/api/admin/units",
                new { count = created.Count, codes = created });
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}", async (Guid id, SaveUnitRequest request, AppDbContext db) =>
        {
            var unit = await db.SiteUnits.FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null) return Results.NotFound();

            if (request.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Obyekt tapılmadı.");

            try
            {
                unit.Place(request.SiteId, request.Latitude, request.Longitude, request.InstalledOn);
                if (Enum.TryParse<UnitStatus>(request.Status, ignoreCase: true, out var status))
                    unit.SetStatus(status);
                unit.SetNote(request.Note);
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

        // Xəritədə nöqtəni dəqiqləşdirmək — tez-tez işlənən əməliyyat, ayrıca yol
        group.MapPut("/{id:guid}/position", async (
            Guid id, PositionRequest request, AppDbContext db) =>
        {
            var unit = await db.SiteUnits.FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null) return Results.NotFound();

            try
            {
                unit.MoveTo(request.Latitude, request.Longitude);
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
            var unit = await db.SiteUnits.FirstOrDefaultAsync(u => u.Id == id);
            if (unit is null) return Results.NotFound();

            db.SiteUnits.Remove(unit);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanManage)
        .RequireAntiforgery();
    }

    /// <summary>
    /// Nüsxələri obyektin ətrafına səpələyir. Hamısı eyni koordinatda olsa xəritədə
    /// 24 skamya BİR nöqtə kimi görünər və "hansı skamya harada" sualı cavabsız qalar.
    /// Qızıl bucaq spiralı bərabər və təbii paylanma verir; sonra hər nüsxə xəritədən
    /// əl ilə dəqiqləşdirilə bilər.
    /// </summary>
    private static (double? Lat, double? Lng) Scatter(
        double lat, double lng, int index, int? spreadMeters)
    {
        var spread = Math.Clamp(spreadMeters ?? 60, 5, 2000);
        var radius = spread * Math.Sqrt(index + 0.5) / Math.Sqrt(24);
        var angle = index * GoldenAngle;

        // 1 dərəcə enlik ≈ 111 320 m; uzunluqda enliyə görə daralır
        var north = radius * Math.Cos(angle) / 111_320d;
        var east = radius * Math.Sin(angle) / (111_320d * Math.Cos(lat * Math.PI / 180));

        return (Math.Round(lat + north, 6), Math.Round(lng + east, 6));
    }
}

public sealed record BulkCreateUnitsRequest(
    Guid ProductId, Guid? SiteId, int Quantity, DateOnly? InstalledOn,
    string? Note, int? SpreadMeters);

public sealed record SaveUnitRequest(
    Guid? SiteId, double? Latitude, double? Longitude, DateOnly? InstalledOn,
    string? Status, string? Note);

public sealed record PositionRequest(double Latitude, double Longitude);

public sealed record UnitDto(
    Guid Id, string Code, Guid ProductId, string ProductName,
    Guid? SiteId, string? SiteName,
    double? Latitude, double? Longitude, bool HasOwnPosition,
    string Status, DateOnly? InstalledOn, string? Note, DateTime UpdatedAtUtc);
