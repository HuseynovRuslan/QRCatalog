using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Infrastructure.Qr;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class QrCodeEndpoints
{
    private const string FallbackPrefix = "QR";
    private const int MaxCreateRetries = 3;

    public static void MapQrCodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/qrcodes");

        group.MapGet("/", async (AppDbContext db, string? search, Guid? targetId,
            int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.QrCodes.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(q => q.HumanCode.Contains(search.ToUpperInvariant()));
            // Məhsul səhifəsi öz kodlarını göstərir — "bu modelin etiketi hansıdır"
            // sualına cavab kodlar siyahısında ad axtarmaqla verilməməlidir
            if (targetId is { } target)
                query = query.Where(q => q.TargetId == target);

            var total = await query.CountAsync();
            var codes = await query
                .OrderByDescending(q => q.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(q => new QrCodeDto(
                    q.Id, q.Token, q.HumanCode,
                    q.TargetType.ToString(), q.TargetId,
                    q.Status.ToString(), q.CreatedAtUtc))
                .ToListAsync();

            var names = await TargetNamesAsync(db,
                codes.Select(c => (c.TargetType, c.TargetId)));

            var items = codes
                .Select(c => c with
                {
                    TargetName = c.TargetId is { } id ? names.GetValueOrDefault(id) : null,
                })
                .ToList();

            return Results.Ok(new PagedResult<QrCodeDto>(items, total, page, pageSize));
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPost("/", async (
            CreateQrCodeRequest request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            if (!Enum.TryParse<QrTargetType>(request.TargetType, ignoreCase: true, out var targetType))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Hədəf növü tanınmadı.");

            string prefix = FallbackPrefix;
            string targetName = "Arxiv";
            if (targetType == QrTargetType.Category)
            {
                var category = await db.Categories.AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == request.TargetId);
                if (category is null)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Hədəf kateqoriya tapılmadı.");
                prefix = category.CodePrefix ?? FallbackPrefix;
                targetName = category.Name;
            }
            else if (targetType == QrTargetType.Product)
            {
                var product = await db.Products.AsNoTracking()
                    .Where(p => p.Id == request.TargetId)
                    .Select(p => new
                    {
                        Name = p.Translations
                            .Where(t => t.Lang == "az").Select(t => t.Name).FirstOrDefault(),
                        p.CategoryId,
                    })
                    .FirstOrDefaultAsync();
                if (product is null)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Hədəf məhsul tapılmadı.");

                // Prefiks məhsulun kateqoriyasından gəlir — SZ-0142-dəki "SZ"
                var codePrefix = await db.Categories.AsNoTracking()
                    .Where(c => c.Id == product.CategoryId)
                    .Select(c => c.CodePrefix)
                    .FirstOrDefaultAsync();
                prefix = codePrefix ?? FallbackPrefix;
                targetName = product.Name ?? "(adsız)";
            }

            // Nömrə yarışı: unikal indeks + təkrar cəhd. Admin kod yaratması nadir əməliyyatdır,
            // FOR UPDATE kilidinə ehtiyac yoxdur.
            for (var attempt = 1; ; attempt++)
            {
                var next = await db.QrCodes
                    .Where(q => q.Prefix == prefix)
                    .Select(q => (int?)q.Sequence)
                    .MaxAsync() ?? 0;

                var qrCode = QrCode.Create(companyId, TokenGenerator.Next(), prefix, next + 1,
                    targetType, targetType == QrTargetType.Archive ? null : request.TargetId);
                db.QrCodes.Add(qrCode);

                try
                {
                    await db.SaveChangesAsync();
                    return Results.Created($"/api/admin/qrcodes/{qrCode.Id}",
                        new QrCodeDto(qrCode.Id, qrCode.Token, qrCode.HumanCode,
                            qrCode.TargetType.ToString(), qrCode.TargetId,
                            qrCode.Status.ToString(), qrCode.CreatedAtUtc, targetName));
                }
                catch (DbUpdateException) when (attempt < MaxCreateRetries)
                {
                    db.Entry(qrCode).State = EntityState.Detached; // paralel yaratma toqquşdu — yenidən
                }
            }
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/retarget", async (
            Guid id, RetargetRequest request, AppDbContext db) =>
        {
            var qrCode = await db.QrCodes.FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode is null) return Results.NotFound();

            if (!Enum.TryParse<QrTargetType>(request.TargetType, ignoreCase: true, out var targetType))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Hədəf növü tanınmadı.");

            if (targetType == QrTargetType.Category &&
                !await db.Categories.AnyAsync(c => c.Id == request.TargetId))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Hədəf kateqoriya tapılmadı.");
            }

            if (targetType == QrTargetType.Product &&
                !await db.Products.AnyAsync(p => p.Id == request.TargetId))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Hədəf məhsul tapılmadı.");
            }

            try
            {
                qrCode.Retarget(targetType,
                    targetType == QrTargetType.Archive ? null : request.TargetId);
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

        group.MapPost("/{id:guid}/retire", async (Guid id, AppDbContext db) =>
        {
            var qrCode = await db.QrCodes.FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode is null) return Results.NotFound();
            qrCode.Retire();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPost("/{id:guid}/activate", async (Guid id, AppDbContext db) =>
        {
            var qrCode = await db.QrCodes.FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode is null) return Results.NotFound();
            qrCode.Reactivate();
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapGet("/{id:guid}/image.svg", async (
            Guid id, AppDbContext db, QrImageService images, HttpContext http,
            IConfiguration config) =>
        {
            var qrCode = await db.QrCodes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode is null) return Results.NotFound();

            var svg = images.RenderSvg(PublicUrl(config, http, qrCode.Token));
            return Results.Text(svg, "image/svg+xml");
        })
        .RequireAuthorization(Policies.CanView);

        group.MapGet("/{id:guid}/image.png", async (
            Guid id, AppDbContext db, QrImageService images, HttpContext http,
            IConfiguration config) =>
        {
            var qrCode = await db.QrCodes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id);
            if (qrCode is null) return Results.NotFound();

            var png = images.RenderPng(PublicUrl(config, http, qrCode.Token));
            return Results.File(png, "image/png", $"{qrCode.HumanCode}.png");
        })
        .RequireAuthorization(Policies.CanView);

        // Seçilmiş kodlar üçün A4 çap vərəqi
        group.MapPost("/sheet", async (
            SheetRequest request, AppDbContext db, LabelSheetService sheets, HttpContext http,
            IConfiguration config) =>
        {
            var codes = await db.QrCodes.AsNoTracking()
                .Where(q => request.Ids.Contains(q.Id))
                .OrderBy(q => q.HumanCode)
                .ToListAsync();
            if (codes.Count == 0)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Çap üçün kod seçilməyib.");

            var names = await TargetNamesAsync(db,
                codes.Select(q => (q.TargetType.ToString(), q.TargetId)));

            var items = codes
                .Select(q => new LabelItem(
                    q.HumanCode,
                    PublicUrl(config, http, q.Token),
                    q.TargetId is { } id ? names.GetValueOrDefault(id, "") : "Arxiv"))
                .ToList();

            var pdf = sheets.RenderSheet(items);
            return Results.File(pdf, "application/pdf", "qr-etiketler.pdf");
        })
        .RequireAuthorization(Policies.CanView)
        .RequireAntiforgery();
    }

    /// <summary>Kateqoriya və məhsul hədəflərinin adlarını bir sorğu dəsti ilə yığır.</summary>
    private static async Task<Dictionary<Guid, string>> TargetNamesAsync(
        AppDbContext db, IEnumerable<(string TargetType, Guid? TargetId)> targets)
    {
        var byType = targets
            .Where(t => t.TargetId is not null)
            .ToLookup(t => t.TargetType, t => t.TargetId!.Value);

        var names = new Dictionary<Guid, string>();

        var categoryIds = byType[nameof(QrTargetType.Category)].Distinct().ToList();
        if (categoryIds.Count > 0)
        {
            foreach (var (id, name) in await db.Categories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name))
            {
                names[id] = name;
            }
        }

        var productIds = byType[nameof(QrTargetType.Product)].Distinct().ToList();
        if (productIds.Count > 0)
        {
            foreach (var (id, name) in await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    Name = p.Translations
                        .Where(t => t.Lang == "az").Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
                })
                .ToDictionaryAsync(p => p.Id, p => p.Name))
            {
                names[id] = name;
            }
        }

        return names;
    }

    /// <summary>
    /// QR-ın içinə düşən URL. Produksiyada Qr:PublicBaseUrl konfiqurasiyasından gəlir —
    /// bu, etiketə çap olunur və domen qərarına (S2-01) bağlıdır; dev-də cari host işlədilir.
    /// </summary>
    private static string PublicUrl(IConfiguration config, HttpContext http, string token)
    {
        var baseUrl = config["Qr:PublicBaseUrl"]?.TrimEnd('/')
            ?? $"{http.Request.Scheme}://{http.Request.Host}";
        return $"{baseUrl}/q/{token}";
    }
}

public sealed record QrCodeDto(
    Guid Id, string Token, string HumanCode, string TargetType, Guid? TargetId,
    string Status, DateTime CreatedAtUtc, string? TargetName = null);

public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize);

public sealed record CreateQrCodeRequest(string TargetType, Guid? TargetId);

public sealed record RetargetRequest(string TargetType, Guid? TargetId);

public sealed record SheetRequest(List<Guid> Ids);
