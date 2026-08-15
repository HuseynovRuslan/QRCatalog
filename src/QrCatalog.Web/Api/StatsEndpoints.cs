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
            {
                queue.TryEnqueue(new PendingScan(
                    qr.CompanyId, qr.Id,
                    DeviceKindParser.Parse(http.Request.Headers.UserAgent),
                    FirstLanguage(http.Request.Headers.AcceptLanguage)));
            }

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

            // Ən çox skan olunan məhsullar (30 gün)
            var topRaw = await db.ScanEvents
                .Where(s => s.OccurredAtUtc >= since)
                .Join(db.QrCodes, s => s.QrCodeId, q => q.Id, (s, q) => q)
                .Where(q => q.TargetType == QrTargetType.Product && q.TargetId != null)
                .GroupBy(q => q.TargetId!.Value)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

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

            var byDay = await db.ScanEvents
                .Where(s => s.OccurredAtUtc >= since)
                .GroupBy(s => s.OccurredAtUtc.Date)
                .Select(g => new DayCountDto(g.Key, g.Count()))
                .OrderBy(d => d.Date)
                .ToListAsync();

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
                return new CodeCountDto(
                    info?.HumanCode ?? "?",
                    info?.TargetId is { } tid ? productNames.GetValueOrDefault(tid) : null,
                    c.Count);
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

    private static string? FirstLanguage(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)) return null;
        var first = acceptLanguage.Split(',')[0].Split(';')[0].Trim();
        return first.Length is > 0 and <= 10 ? first : null;
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
