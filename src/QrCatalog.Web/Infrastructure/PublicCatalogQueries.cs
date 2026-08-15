using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Pages;

namespace QrCatalog.Web.Infrastructure;

/// <summary>
/// Public səhifələrin sorğuları. Qonağın tenant konteksti yoxdur, ona görə bu sorğular
/// tenant filtrini QƏSDƏN keçir və CompanyId-ni HƏMİŞƏ açıq şərtlə verir —
/// bu, /q/{token} ilə birlikdə IgnoreQueryFilters-in icazəli olduğu ikinci və sonuncu yerdir.
/// Yalnız Published məhsullar və yalnız public sahələr qaytarılır.
/// </summary>
public static class PublicCatalogQueries
{
    private const string Lang = "az";

    public sealed record ProductLoad(ProductStatus Status, Guid CategoryId, PublicProductVm? Product);

    public static async Task<ProductLoad?> LoadProductAsync(
        AppDbContext db, IFileStorage storage, Guid companyId,
        Guid? id = null, string? slug = null, string? humanCode = null, string? qrToken = null,
        CancellationToken ct = default)
    {
        var query = db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CompanyId == companyId);
        query = id is not null ? query.Where(p => p.Id == id) : query.Where(p => p.Slug == slug);

        var row = await query
            .Select(p => new
            {
                p.Status,
                p.CategoryId,
                p.Slug,
                Name = p.Translations.Where(t => t.Lang == Lang).Select(t => t.Name).FirstOrDefault(),
                Description = p.Translations.Where(t => t.Lang == Lang)
                    .Select(t => t.Description).FirstOrDefault(),
                Specs = p.Specs.OrderBy(s => s.SortOrder)
                    .Select(s => new { s.Label, s.Value }).ToList(),
                Images = p.Images.OrderBy(i => i.SortOrder)
                    .Select(i => new { i.AltText, i.StoragePrefix, i.Widths }).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (row is null)
            return null;

        if (row.Status != ProductStatus.Published)
            return new ProductLoad(row.Status, row.CategoryId, null);

        var category = await db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Id == row.CategoryId)
            .Select(c => new { c.Name, c.Slug })
            .FirstOrDefaultAsync(ct);

        var company = await db.Companies.AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Phone, c.WhatsappNumber })
            .FirstAsync(ct);

        var vm = new PublicProductVm(
            row.Name ?? "(adsız)",
            row.Description,
            row.Slug,
            category?.Name ?? "Kataloq",
            category?.Slug ?? "",
            row.Specs.Select(s => new PublicSpecVm(s.Label, s.Value)).ToList(),
            row.Images.Select(i => new PublicImageVm(
                i.AltText,
                ParseWidths(i.Widths)
                    .Select(w => new PublicImageVariantVm(
                        w, storage.GetPublicUrl($"{i.StoragePrefix}/w{w}.webp")))
                    .ToList()))
                .ToList(),
            humanCode,
            company.Phone,
            company.WhatsappNumber,
            qrToken);

        return new ProductLoad(row.Status, row.CategoryId, vm);
    }

    public static async Task<List<PublicProductCardVm>> SimilarProductsAsync(
        AppDbContext db, IFileStorage storage, Guid companyId, Guid categoryId,
        Guid? excludeId = null, int take = 4, CancellationToken ct = default)
    {
        var (items, _) = await ProductCardsAsync(db, storage, companyId,
            categoryId: categoryId, search: null, page: 1, pageSize: take, excludeId: excludeId, ct: ct);
        return items;
    }

    public static async Task<(List<PublicProductCardVm> Items, int Total)> ProductCardsAsync(
        AppDbContext db, IFileStorage storage, Guid companyId,
        Guid? categoryId, string? search, int page, int pageSize,
        Guid? excludeId = null, CancellationToken ct = default)
    {
        var query = db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.Status == ProductStatus.Published);

        if (categoryId is { } cid)
            query = query.Where(p => p.CategoryId == cid);
        if (excludeId is { } eid)
            query = query.Where(p => p.Id != eid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            // tsvector ifadəsi migration-dakı GIN indeksi ilə EYNİ formadadır — planner indeksi tutur
            query = query.Where(p => p.Translations.Any(t =>
                t.Lang == Lang &&
                EF.Functions.ToTsVector("simple", t.Name + " " + (t.Description ?? ""))
                    .Matches(EF.Functions.WebSearchToTsQuery("simple", term))));
        }

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(p => p.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Slug,
                p.CategoryId,
                Name = p.Translations.Where(t => t.Lang == Lang).Select(t => t.Name).FirstOrDefault(),
                Thumb = p.Images.OrderBy(i => i.SortOrder)
                    .Select(i => new { i.StoragePrefix, i.Widths }).FirstOrDefault(),
            })
            .ToListAsync(ct);

        var categoryNames = await db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var items = rows.Select(r =>
        {
            var widths = r.Thumb is null ? [] : ParseWidths(r.Thumb.Widths);
            var thumbWidth = widths.Where(w => w >= 640).DefaultIfEmpty(widths.LastOrDefault()).First();
            return new PublicProductCardVm(
                r.Name ?? "(adsız)",
                r.Slug,
                categoryNames.GetValueOrDefault(r.CategoryId, ""),
                r.Thumb is null || thumbWidth == 0
                    ? null
                    : storage.GetPublicUrl($"{r.Thumb.StoragePrefix}/w{thumbWidth}.webp"),
                r.Thumb is null
                    ? null
                    : string.Join(", ", widths.Select(w =>
                        $"{storage.GetPublicUrl($"{r.Thumb.StoragePrefix}/w{w}.webp")} {w}w")));
        }).ToList();

        return (items, total);
    }

    public static async Task<List<PublicCategoryVm>> CategoriesAsync(
        AppDbContext db, Guid companyId, Guid? parentId, CancellationToken ct = default)
    {
        return await db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.ParentId == parentId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new PublicCategoryVm(
                c.Name, c.Slug, c.Description,
                db.Products.IgnoreQueryFilters().Count(p =>
                    p.CompanyId == companyId && p.CategoryId == c.Id &&
                    p.Status == ProductStatus.Published)))
            .ToListAsync(ct);
    }

    private static List<int> ParseWidths(string widths) =>
        widths.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}
