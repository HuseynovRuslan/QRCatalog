using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

/// <summary>
/// Quraşdırılma obyektləri — məhsulların fiziki durduğu yerlər və xəritə nöqtələri.
/// Obyekt SİLİNƏ BİLƏR (məhsuldan fərqli olaraq): səhv daxil edilmiş ünvan tarixi
/// məlumat deyil, sadəcə səhvdir. Silinmə audit jurnalına düşür.
/// </summary>
public static class SiteEndpoints
{
    private const string DefaultLang = "az";

    public static void MapSiteEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/sites");

        group.MapGet("/", async (AppDbContext db, string? search) =>
        {
            var query = db.Sites.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(s =>
                    EF.Functions.ILike(s.Name, $"%{search}%") ||
                    (s.Address != null && EF.Functions.ILike(s.Address, $"%{search}%")));

            var sites = await query
                .OrderBy(s => s.Name)
                .Select(s => new
                {
                    s.Id, s.Name, Kind = s.Kind.ToString(), s.Address,
                    s.Latitude, s.Longitude, s.ContactName, s.ContactPhone, s.Note,
                    s.UpdatedAtUtc,
                    Items = s.Items.Select(i => new { i.Id, i.ProductId, i.Quantity, i.InstalledOn })
                        .ToList(),
                })
                .ToListAsync();

            // Məhsul adları bir sorğu ilə — xəritə balonunda "3 × Park skamyası" yazılır
            var productIds = sites.SelectMany(s => s.Items).Select(i => i.ProductId).Distinct().ToList();
            var productNames = productIds.Count == 0
                ? []
                : await db.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        Name = p.Translations.Where(t => t.Lang == DefaultLang)
                            .Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
                    })
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

            var items = sites.Select(s => new SiteDto(
                s.Id, s.Name, s.Kind, s.Address, s.Latitude, s.Longitude,
                s.ContactName, s.ContactPhone, s.Note, s.UpdatedAtUtc,
                s.Items.Select(i => new SiteItemDto(
                    i.Id, i.ProductId,
                    productNames.GetValueOrDefault(i.ProductId, "(silinmiş məhsul)"),
                    i.Quantity, i.InstalledOn)).ToList(),
                s.Items.Sum(i => i.Quantity))).ToList();

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

        // Obyektdəki məhsul sətirləri tam əvəz olunur — sətir-sətir redaktə əvəzinə
        // bütöv göndərmək UI-da sadədir və yarımçıq vəziyyət yaratmır
        group.MapPut("/{id:guid}/items", async (
            Guid id, ReplaceSiteItemsRequest request, AppDbContext db) =>
        {
            var site = await db.Sites.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
            if (site is null) return Results.NotFound();

            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var known = await db.Products.Where(p => productIds.Contains(p.Id)).CountAsync();
            if (known != productIds.Count)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Siyahıda tanınmayan məhsul var.");

            try
            {
                var fresh = request.Items
                    .Select(i => SiteItem.Create(site.Id, i.ProductId, i.Quantity, i.InstalledOn))
                    .ToList();
                db.RemoveRange(site.Items);
                site.Items.Clear();
                site.Items.AddRange(fresh);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            site.Touch();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var site = await db.Sites.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
            if (site is null) return Results.NotFound();

            db.RemoveRange(site.Items);
            db.Sites.Remove(site);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanManage)
        .RequireAntiforgery();
    }

    private static SiteKind ParseKind(string? kind) =>
        Enum.TryParse<SiteKind>(kind, ignoreCase: true, out var parsed) ? parsed : SiteKind.Other;
}

public sealed record SaveSiteRequest(
    string Name, string? Kind, double Latitude, double Longitude,
    string? Address, string? ContactName, string? ContactPhone, string? Note);

public sealed record SiteItemInput(Guid ProductId, int Quantity, DateOnly? InstalledOn);

public sealed record ReplaceSiteItemsRequest(List<SiteItemInput> Items);

public sealed record SiteItemDto(
    Guid Id, Guid ProductId, string ProductName, int Quantity, DateOnly? InstalledOn);

public sealed record SiteDto(
    Guid Id, string Name, string Kind, string? Address, double Latitude, double Longitude,
    string? ContactName, string? ContactPhone, string? Note, DateTime UpdatedAtUtc,
    List<SiteItemDto> Items, int TotalQuantity);
