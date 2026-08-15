using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Application.Common;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Images;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class ProductEndpoints
{
    private const string DefaultLang = "az";

    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/products");

        group.MapGet("/", async (
            AppDbContext db, IFileStorage storage,
            string? search, Guid? categoryId, string? status,
            int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.Products.AsNoTracking();

            if (categoryId is { } cid)
                query = query.Where(p => p.CategoryId == cid);
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<ProductStatus>(status, ignoreCase: true, out var parsedStatus))
                query = query.Where(p => p.Status == parsedStatus);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p =>
                    p.Translations.Any(t => t.Lang == DefaultLang &&
                        EF.Functions.ILike(t.Name, $"%{term}%")) ||
                    (p.Sku != null && EF.Functions.ILike(p.Sku, $"%{term}%")));
            }

            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(p => p.UpdatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    Name = p.Translations
                        .Where(t => t.Lang == DefaultLang)
                        .Select(t => t.Name)
                        .FirstOrDefault(),
                    p.CategoryId,
                    p.Sku,
                    Status = p.Status.ToString(),
                    p.UpdatedAtUtc,
                    Thumb = p.Images
                        .OrderBy(i => i.SortOrder)
                        .Select(i => new { i.StoragePrefix, i.Widths })
                        .FirstOrDefault(),
                })
                .ToListAsync();

            var categoryNames = await db.Categories.AsNoTracking()
                .ToDictionaryAsync(c => c.Id, c => c.Name);

            var items = rows.Select(r => new ProductListItem(
                r.Id,
                r.Name ?? "(adsız)",
                categoryNames.GetValueOrDefault(r.CategoryId, "—"),
                r.Sku,
                r.Status,
                r.UpdatedAtUtc,
                r.Thumb is null
                    ? null
                    : storage.GetPublicUrl($"{r.Thumb.StoragePrefix}/w{MinWidth(r.Thumb.Widths)}.webp")))
                .ToList();

            return Results.Ok(new PagedResult<ProductListItem>(items, total, page, pageSize));
        })
        .RequireAuthorization(Policies.CanView);

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, IFileStorage storage) =>
        {
            var product = await db.Products.AsNoTracking()
                .Include(p => p.Translations)
                .Include(p => p.Specs)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();

            return Results.Ok(ToDetail(product, storage));
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPost("/", async (
            SaveProductRequest request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Məhsul adı boş ola bilməz.");
            if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Kateqoriya tapılmadı.");

            var slug = await UniqueSlugAsync(db, SlugGenerator.FromName(request.Name), excludeId: null);

            Product product;
            try
            {
                product = Product.Create(companyId, request.CategoryId, slug, request.Sku);
                product.Translations.Add(ProductTranslation.Create(
                    product.Id, DefaultLang, request.Name, request.Description));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            db.Products.Add(product);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/products/{product.Id}", new { product.Id });
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id, SaveProductRequest request, AppDbContext db) =>
        {
            var product = await db.Products
                .Include(p => p.Translations)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Məhsul adı boş ola bilməz.");
            if (!await db.Categories.AnyAsync(c => c.Id == request.CategoryId))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Kateqoriya tapılmadı.");

            try
            {
                product.Update(request.CategoryId, request.Sku);
                var az = product.Translations.FirstOrDefault(t => t.Lang == DefaultLang);
                if (az is null)
                    product.Translations.Add(ProductTranslation.Create(
                        product.Id, DefaultLang, request.Name, request.Description));
                else
                    az.Update(request.Name, request.Description);
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

        // Status keçidləri. Silmə endpoint-i QƏSDƏN yoxdur — məhsul arxivləşir, silinmir:
        // satılmış nüsxələrin QR-ları həmişə bir səhifəyə çıxmalıdır.
        MapStatusTransition(group, "publish", p => p.Publish());
        MapStatusTransition(group, "unpublish", p => p.Unpublish());
        MapStatusTransition(group, "archive", p => p.Archive());

        group.MapPost("/{id:guid}/copy", async (Guid id, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            var source = await db.Products.AsNoTracking()
                .Include(p => p.Translations)
                .Include(p => p.Specs)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (source is null) return Results.NotFound();

            var azName = source.Translations.FirstOrDefault(t => t.Lang == DefaultLang)?.Name ?? "(adsız)";
            var copyName = $"{azName} (kopya)";
            var slug = await UniqueSlugAsync(db, SlugGenerator.FromName(copyName), excludeId: null);

            var copy = Product.Create(companyId, source.CategoryId, slug, source.Sku);
            foreach (var t in source.Translations)
                copy.Translations.Add(ProductTranslation.Create(copy.Id, t.Lang,
                    t.Lang == DefaultLang ? copyName : t.Name, t.Description));
            foreach (var s in source.Specs.OrderBy(s => s.SortOrder))
                copy.Specs.Add(ProductSpec.Create(copy.Id, s.Label, s.Value, s.SortOrder));
            // Şəkillər kopyalanmır: storage obyektlərini paylaşmaq idarəni dolaşdırır,
            // kopya adətən fərqli rəng/ölçü üçündür və öz şəkli olacaq.

            db.Products.Add(copy);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/products/{copy.Id}", new { copy.Id });
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/specs", async (
            Guid id, ReplaceSpecsRequest request, AppDbContext db) =>
        {
            var product = await db.Products
                .Include(p => p.Specs)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();

            try
            {
                product.Specs.Clear();
                var order = 0;
                foreach (var item in request.Specs)
                    product.Specs.Add(ProductSpec.Create(product.Id, item.Label, item.Value, order++));
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            product.Touch();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        MapImageEndpoints(group);
    }

    private static void MapStatusTransition(
        RouteGroupBuilder group, string action, Action<Product> transition)
    {
        group.MapPost($"/{{id:guid}}/{action}", async (Guid id, AppDbContext db) =>
        {
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();
            transition(product);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();
    }

    private static void MapImageEndpoints(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/images", async (
            Guid id, HttpRequest httpRequest, AppDbContext db,
            ImageProcessor processor, IFileStorage storage) =>
        {
            var product = await db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();

            if (!httpRequest.HasFormContentType)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "multipart/form-data gözlənilir.");

            var form = await httpRequest.ReadFormAsync();
            if (form.Files.Count == 0)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Fayl seçilməyib.");

            var created = new List<object>();
            var nextOrder = product.Images.Count == 0
                ? 0
                : product.Images.Max(i => i.SortOrder == int.MaxValue ? 0 : i.SortOrder) + 1;

            foreach (var file in form.Files)
            {
                if (file.Length == 0 || file.Length > ImageProcessor.MaxUploadBytes)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: $"\"{file.FileName}\" boşdur və ya 15 MB-dan böyükdür.");

                await using var stream = file.OpenReadStream();
                var head = new byte[12];
                var read = await stream.ReadAtLeastAsync(head, 12, throwOnEndOfStream: false);
                if (!processor.LooksLikeSupportedImage(head.AsSpan(0, read)))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: $"\"{file.FileName}\" şəkil deyil — JPEG, PNG və ya WebP gözlənilir.");
                stream.Position = 0;

                ProcessResult result;
                try
                {
                    result = await processor.ProcessAsync(stream);
                }
                catch (Exception)
                {
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: $"\"{file.FileName}\" oxuna bilmədi — fayl korlanmış ola bilər.");
                }

                var imageId = Guid.NewGuid();
                var image = ProductImage.Create(imageId, product.Id,
                    $"products/{product.Id}/{imageId}",
                    result.Variants.Select(v => v.Width));
                image.SetSortOrder(nextOrder++);

                foreach (var variant in result.Variants)
                {
                    await using var ms = new MemoryStream(variant.WebpBytes);
                    await storage.SaveAsync($"{image.StoragePrefix}/w{variant.Width}.webp", ms, "image/webp");
                }

                product.Images.Add(image);
                created.Add(new { image.Id });
            }

            product.Touch();
            await db.SaveChangesAsync();
            return Results.Ok(created);
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery(); // SPA FormData ilə də X-XSRF-TOKEN başlığı göndərir

        group.MapPut("/{id:guid}/images/reorder", async (
            Guid id, ReorderRequest request, AppDbContext db) =>
        {
            var product = await db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return Results.NotFound();

            var byId = product.Images.ToDictionary(i => i.Id);
            if (request.OrderedIds.Any(imageId => !byId.ContainsKey(imageId)))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Sıralamada tanınmayan şəkil var.");

            for (var i = 0; i < request.OrderedIds.Count; i++)
                byId[request.OrderedIds[i]].SetSortOrder(i);

            product.Touch();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/images/{imageId:guid}", async (
            Guid id, Guid imageId, UpdateImageRequest request, AppDbContext db) =>
        {
            var product = await db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
            var image = product?.Images.FirstOrDefault(i => i.Id == imageId);
            if (image is null) return Results.NotFound();

            image.SetAltText(request.AltText);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapDelete("/{id:guid}/images/{imageId:guid}", async (
            Guid id, Guid imageId, AppDbContext db, IFileStorage storage) =>
        {
            var product = await db.Products
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);
            var image = product?.Images.FirstOrDefault(i => i.Id == imageId);
            if (product is null || image is null) return Results.NotFound();

            product.Images.Remove(image);
            await storage.DeleteByPrefixAsync(image.StoragePrefix);
            product.Touch();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();
    }

    private static async Task<string> UniqueSlugAsync(AppDbContext db, string baseSlug, Guid? excludeId)
    {
        var slug = baseSlug;
        for (var i = 2;
             await db.Products.AnyAsync(p => p.Slug == slug && (excludeId == null || p.Id != excludeId));
             i++)
        {
            slug = $"{baseSlug}-{i}";
        }
        return slug;
    }

    private static int MinWidth(string widths) =>
        widths.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).Min();

    private static ProductDetail ToDetail(Product product, IFileStorage storage)
    {
        var az = product.Translations.FirstOrDefault(t => t.Lang == DefaultLang);
        return new ProductDetail(
            product.Id,
            az?.Name ?? "(adsız)",
            az?.Description,
            product.CategoryId,
            product.Sku,
            product.Slug,
            product.Status.ToString(),
            product.Specs.OrderBy(s => s.SortOrder)
                .Select(s => new SpecItem(s.Label, s.Value)).ToList(),
            product.Images.OrderBy(i => i.SortOrder).Select(i => new ImageItem(
                i.Id,
                i.AltText,
                i.WidthList().Select(w => new ImageVariant(
                    w, storage.GetPublicUrl($"{i.StoragePrefix}/w{w}.webp"))).ToList()))
                .ToList());
    }
}

public sealed record ProductListItem(
    Guid Id, string Name, string CategoryName, string? Sku, string Status,
    DateTime UpdatedAtUtc, string? ThumbnailUrl);

public sealed record ProductDetail(
    Guid Id, string Name, string? Description, Guid CategoryId, string? Sku, string Slug,
    string Status, List<SpecItem> Specs, List<ImageItem> Images);

public sealed record SpecItem(string Label, string Value);

public sealed record ImageItem(Guid Id, string? AltText, List<ImageVariant> Variants);

public sealed record ImageVariant(int Width, string Url);

public sealed record SaveProductRequest(
    string Name, string? Description, Guid CategoryId, string? Sku);

public sealed record ReplaceSpecsRequest(List<SpecItem> Specs);

public sealed record UpdateImageRequest(string? AltText);
