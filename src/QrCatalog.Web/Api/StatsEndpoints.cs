using Microsoft.EntityFrameworkCore;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Infrastructure.Scans;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class StatsEndpoints
{
    private const string Lang = "az";

    public static void MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        // Skan qeydiyyatı — Q səhifəsindəki JS beacon bura vurur. Səhifənin özü
        // keşləndiyi üçün qeyd BURADA gedir: keşlənmiş cavab da sayılır, JS işlətməyən
        // botlar isə süzülür. Public token həlli — sənədləşdirilmiş cross-tenant istisna.
        app.MapPost("/api/public/scans", async (
            ScanBeaconRequest request, AppDbContext db, ScanEventQueue queue, HttpContext http) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 24)
                return Results.NoContent(); // heç bir məlumat sızdırmırıq

            var qr = await db.QrCodes.IgnoreQueryFilters().AsNoTracking()
                .Where(q => q.Token == request.Token)
                .Select(q => new { q.Id, q.CompanyId })
                .FirstOrDefaultAsync();

            if (qr is not null)
                ScanRecorder.Record(queue, http, qr.CompanyId, qr.Id);

            return Results.NoContent(); // tapılıb-tapılmamasından asılı olmayaraq eyni cavab
        })
        .RequireRateLimiting("qr-resolve");

        var group = app.MapGroup("/api/admin/stats");

        group.MapGet("/dashboard", async (AppDbContext db) =>
        {
            var since = DateTime.UtcNow.AddDays(-30);

            var productsTotal = await db.Products.CountAsync();
            var productsPublished = await db.Products
                .CountAsync(p => p.Status == ProductStatus.Published);
            var newInquiries = await db.Inquiries.CountAsync(i => i.Status == InquiryStatus.New);
            var scans30d = await db.ScanEvents.CountAsync(s => s.OccurredAtUtc >= since);

            // Ən çox skan olunan məhsullar (30 gün).
            //
            // Vahid kodları da bura düşür: etiket nüsxəyə bağlıdır, amma müştəri
            // marağı MODELƏ aiddir. Bu qol olmasa vahid etiketləri işə düşən kimi
            // statistika boşalar — skanlar sayılar, «hansı model» sütunu isə boş
            // qalar və rəhbərlik hesabatın sındığını düşünər.
            var scanTargets = await db.ScanEvents
                .Where(s => s.OccurredAtUtc >= since)
                .Join(db.QrCodes, s => s.QrCodeId, q => q.Id, (s, q) => q)
                .Where(q => q.TargetId != null &&
                            (q.TargetType == QrTargetType.Product ||
                             q.TargetType == QrTargetType.Unit))
                .Select(q => new { q.TargetType, TargetId = q.TargetId!.Value })
                .ToListAsync();

            var unitTargetIds = scanTargets
                .Where(t => t.TargetType == QrTargetType.Unit)
                .Select(t => t.TargetId).Distinct().ToList();
            var unitToProduct = unitTargetIds.Count == 0
                ? []
                : await db.SiteUnits
                    .Where(u => unitTargetIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.ProductId })
                    .ToDictionaryAsync(u => u.Id, u => u.ProductId);

            var topRaw = scanTargets
                .Select(t => t.TargetType == QrTargetType.Unit
                    ? unitToProduct.GetValueOrDefault(t.TargetId)
                    : t.TargetId)
                .Where(id => id != Guid.Empty)
                .GroupBy(id => id)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            var topIds = topRaw.Select(t => t.ProductId).ToList();
            var names = await db.Products
                .Where(p => topIds.Contains(p.Id))
                .Select(p => new
                {
                    p.Id,
                    Name = p.Translations.Where(t => t.Lang == Lang)
                        .Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
                })
                .ToDictionaryAsync(p => p.Id, p => p.Name);

            var unscannedCount = await db.QrCodes
                .CountAsync(q => q.Status == QrCodeStatus.Active &&
                    !db.ScanEvents.Any(s => s.QrCodeId == q.Id));

            return Results.Ok(new DashboardDto(
                productsTotal, productsPublished, newInquiries, scans30d,
                topRaw.Select(t => new TopProductDto(
                    names.GetValueOrDefault(t.ProductId, "(adsız)"), t.Count)).ToList(),
                unscannedCount));
        })
        .RequireAuthorization(Policies.CanView);

        group.MapGet("/scans", async (AppDbContext db, int days = 30) =>
        {
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

            // DateTime.Date PostgreSQL-ə tərcümə OLUNMUR (timestamptz) — il/ay/gün ayrı-ayrı
            // date_part-lara düşür və tarix müştəri tərəfdə yığılır. Qruplaşdırma bazanın
            // saat qurşağına görə gedir; server UTC-dədir (ops/README) — bütün damğalar da UTC.
            var byDayRaw = await db.ScanEvents
                .Where(s => s.OccurredAtUtc >= since)
                .GroupBy(s => new { s.OccurredAtUtc.Year, s.OccurredAtUtc.Month, s.OccurredAtUtc.Day })
                .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, Count = g.Count() })
                .ToListAsync();

            var byDay = byDayRaw
                .Select(d => new DayCountDto(
                    new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc), d.Count))
                .OrderBy(d => d.Date)
                .ToList();

            var byCode = await db.ScanEvents
                .Where(s => s.OccurredAtUtc >= since)
                .GroupBy(s => s.QrCodeId)
                .Select(g => new { QrCodeId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(50)
                .ToListAsync();

            var codeIds = byCode.Select(c => c.QrCodeId).ToList();
            var codes = await db.QrCodes
                .Where(q => codeIds.Contains(q.Id))
                .Select(q => new { q.Id, q.HumanCode, q.TargetType, q.TargetId })
                .ToListAsync();

            var productIds = codes
                .Where(c => c.TargetType == QrTargetType.Product && c.TargetId != null)
                .Select(c => c.TargetId!.Value).Distinct().ToList();

            // Vahid kodlarında ad sütunu nüsxə kodudur («SK-PR-3N/007») — sahədə
            // işə yarayan identifikator budur, model adı deyil.
            var unitIds = codes
                .Where(c => c.TargetType == QrTargetType.Unit && c.TargetId != null)
                .Select(c => c.TargetId!.Value).Distinct().ToList();
            var unitCodes = unitIds.Count == 0
                ? []
                : await db.SiteUnits
                    .Where(u => unitIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.Code })
                    .ToDictionaryAsync(u => u.Id, u => u.Code);
            var productNames = productIds.Count == 0
                ? []
                : await db.Products
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        Name = p.Translations.Where(t => t.Lang == Lang)
                            .Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
                    })
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

            var codeInfo = codes.ToDictionary(c => c.Id);
            var byCodeDto = byCode.Select(c =>
            {
                var info = codeInfo.GetValueOrDefault(c.QrCodeId);
                var label = info?.TargetId is { } tid
                    ? (info.TargetType == QrTargetType.Unit
                        ? unitCodes.GetValueOrDefault(tid)
                        : productNames.GetValueOrDefault(tid))
                    : null;
                return new CodeCountDto(info?.HumanCode ?? "?", label, c.Count);
            }).ToList();

            // Heç vaxt skan olunmayan aktiv kodlar — etiket vurulmayıb, ya görünmür
            var unscanned = await db.QrCodes
                .Where(q => q.Status == QrCodeStatus.Active &&
                    !db.ScanEvents.Any(s => s.QrCodeId == q.Id))
                .OrderBy(q => q.CreatedAtUtc)
                .Take(100)
                .Select(q => new UnscannedCodeDto(q.HumanCode, q.CreatedAtUtc))
                .ToListAsync();

            return Results.Ok(new ScanReportDto(byDay, byCodeDto, unscanned));
        })
        .RequireAuthorization(Policies.CanView);
    }

}

public sealed record ScanBeaconRequest(string Token);

public sealed record DashboardDto(
    int ProductsTotal, int ProductsPublished, int NewInquiries, int Scans30d,
    List<TopProductDto> TopProducts, int UnscannedCodes);

public sealed record TopProductDto(string Name, int Count);

public sealed record DayCountDto(DateTime Date, int Count);

public sealed record CodeCountDto(string HumanCode, string? ProductName, int Count);

public sealed record UnscannedCodeDto(string HumanCode, DateTime CreatedAtUtc);

public sealed record ScanReportDto(
    List<DayCountDto> ByDay, List<CodeCountDto> ByCode, List<UnscannedCodeDto> Unscanned);
