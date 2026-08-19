using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Pages;

/// <summary>
/// İŞÇİ/RƏHBƏRLİK EKRANI — `/i/{token}`. Etiketi öz işçimiz skan edəndə açılır.
///
/// Bura qəsdən REDAKTOR DEYİL. Sahədə duran adamın sualı «bu düyməni hara basım»
/// deyil: «bu nədir, neçə dənəmiz var, hara qoymuşuq, nə vaxtdan durur». Admin
/// paneli bu suallara cavab verir, amma məlumat dörd səhifəyə səpələnib və telefonda
/// SPA yüklənməsi gözlədir. Bu səhifə serverdə render olunur — bir sorğu, dərhal açılır.
///
/// Yazma əməliyyatı YOXDUR: rəhbərlik üçün baxış ekranıdır, ona görə Viewer rolu
/// olan istifadəçi də tam görür və heç nəyi təsadüfən poza bilmir.
/// </summary>
[Authorize]
public sealed class IModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;

    public IModel(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public string? HumanCode { get; private set; }
    public bool CanEdit { get; private set; }

    // Məhsul kodu üçün
    public ProductInfoVm? Product { get; private set; }

    // Kateqoriya kodu üçün
    public CategoryInfoVm? Category { get; private set; }

    public string Title => Product?.Name ?? Category?.Name ?? "Məlumat";

    public async Task<IActionResult> OnGetAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 24)
            return NotFound();

        var qrCode = await _db.QrCodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Token == token);

        if (qrCode is null)
            return NotFound();

        // Başqa müəssisənin etiketi: bu ekran daxili məlumat göstərir, ona görə
        // müştəri səhifəsinə göndərilir — orada yalnız public sahələr var.
        if (!Guid.TryParse(User.FindFirst(AppClaims.CompanyId)?.Value, out var companyId) ||
            companyId != qrCode.CompanyId)
            return Redirect($"/q/{token}");

        HumanCode = qrCode.HumanCode;
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Editor);

        return qrCode.TargetType switch
        {
            QrTargetType.Product when qrCode.TargetId is { } productId =>
                await LoadProductAsync(companyId, productId),
            QrTargetType.Category when qrCode.TargetId is { } categoryId =>
                await LoadCategoryAsync(companyId, categoryId),
            _ => Redirect("/admin/qr"),
        };
    }

    private async Task<IActionResult> LoadProductAsync(Guid companyId, Guid productId)
    {
        var product = await _db.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.Id == productId && p.CompanyId == companyId)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Status,
                // Ad tərcümə cədvəlindədir (çoxdilli olacaq) — hazırda yalnız "az"
                Name = p.Translations.Where(t => t.Lang == "az")
                    .Select(t => t.Name).FirstOrDefault(),
                CategoryName = _db.Categories.IgnoreQueryFilters()
                    .Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                ImagePrefix = p.Images.OrderBy(i => i.SortOrder)
                    .Select(i => i.StoragePrefix).FirstOrDefault(),
            })
            .FirstOrDefaultAsync();

        if (product is null)
            return NotFound();

        // Nüsxələr bir sorğuda gətirilir, qruplaşdırma yaddaşda olur: bir modelin
        // nüsxə sayı yüzlərlədir, minlərlə deyil — üç ayrı aqreqat sorğusundan ucuzdur.
        var units = await _db.SiteUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.ProductId == productId && u.CompanyId == companyId)
            .Select(u => new
            {
                u.SiteId,
                u.Code,
                u.Status,
                u.InstalledOn,
                SiteName = _db.Sites.IgnoreQueryFilters()
                    .Where(s => s.Id == u.SiteId).Select(s => s.Name).FirstOrDefault(),
                SiteKind = _db.Sites.IgnoreQueryFilters()
                    .Where(s => s.Id == u.SiteId).Select(s => (SiteKind?)s.Kind).FirstOrDefault(),
                SiteAddress = _db.Sites.IgnoreQueryFilters()
                    .Where(s => s.Id == u.SiteId).Select(s => s.Address).FirstOrDefault(),
            })
            .ToListAsync();

        var sites = units
            .Where(u => u.SiteId is not null && u.Status == UnitStatus.Installed)
            .GroupBy(u => new { u.SiteId, u.SiteName, u.SiteKind, u.SiteAddress })
            .Select(g => new SitePresenceVm(
                g.Key.SiteId!.Value,
                g.Key.SiteName ?? "Adsız obyekt",
                SiteKindLabel(g.Key.SiteKind),
                g.Key.SiteAddress,
                g.Count(),
                g.Where(u => u.InstalledOn is not null).Max(u => u.InstalledOn),
                g.OrderBy(u => u.Code).Select(u => u.Code).ToList()))
            .OrderByDescending(s => s.Count)
            .ThenBy(s => s.Name)
            .ToList();

        var since = DateTime.UtcNow.AddDays(-30);
        var scans30d = await _db.ScanEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(e => e.CompanyId == companyId &&
                             e.OccurredAtUtc >= since &&
                             _db.QrCodes.IgnoreQueryFilters()
                                 .Any(q => q.Id == e.QrCodeId &&
                                           q.TargetType == QrTargetType.Product &&
                                           q.TargetId == productId));

        Product = new ProductInfoVm(
            product.Id,
            product.Name ?? "Adsız məhsul",
            product.Sku,
            product.CategoryName,
            product.Status == ProductStatus.Published,
            product.ImagePrefix is null
                ? null
                : _storage.GetPublicUrl($"{product.ImagePrefix}/w320.webp"),
            Installed: units.Count(u => u.Status == UnitStatus.Installed),
            InStock: units.Count(u => u.Status == UnitStatus.InStock),
            InRepair: units.Count(u => u.Status == UnitStatus.InRepair),
            Removed: units.Count(u => u.Status == UnitStatus.Removed),
            Sites: sites,
            Scans30d: scans30d);

        return Page();
    }

    private async Task<IActionResult> LoadCategoryAsync(Guid companyId, Guid categoryId)
    {
        var category = await _db.Categories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == categoryId && c.CompanyId == companyId)
            .Select(c => new { c.Name })
            .FirstOrDefaultAsync();

        if (category is null)
            return NotFound();

        var products = await _db.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.CategoryId == categoryId && p.CompanyId == companyId &&
                        p.Status != ProductStatus.Archived)
            .Select(p => new CategoryProductVm(
                p.Id,
                p.Translations.Where(t => t.Lang == "az").Select(t => t.Name).FirstOrDefault()
                    ?? "Adsız məhsul",
                p.Sku,
                _db.SiteUnits.IgnoreQueryFilters()
                    .Count(u => u.ProductId == p.Id && u.Status == UnitStatus.Installed),
                _db.SiteUnits.IgnoreQueryFilters()
                    .Where(u => u.ProductId == p.Id && u.Status == UnitStatus.Installed &&
                                u.SiteId != null)
                    .Select(u => u.SiteId)
                    .Distinct()
                    .Count()))
            .OrderByDescending(p => p.InstalledCount)
            .ThenBy(p => p.Name)
            .ToListAsync();

        Category = new CategoryInfoVm(category.Name, products);
        return Page();
    }

    private static string SiteKindLabel(SiteKind? kind) => kind switch
    {
        SiteKind.Park => "Park",
        SiteKind.Hotel => "Otel",
        SiteKind.Cafe => "Kafe",
        SiteKind.School => "Məktəb",
        SiteKind.Residential => "Yaşayış kompleksi",
        SiteKind.Beach => "Çimərlik",
        _ => "Obyekt",
    };
}

public sealed record SitePresenceVm(
    Guid SiteId,
    string Name,
    string Kind,
    string? Address,
    int Count,
    DateOnly? LastInstalledOn,
    List<string> UnitCodes);

public sealed record ProductInfoVm(
    Guid Id,
    string Name,
    string? Sku,
    string? CategoryName,
    bool Published,
    string? ImageUrl,
    int Installed,
    int InStock,
    int InRepair,
    int Removed,
    List<SitePresenceVm> Sites,
    int Scans30d)
{
    /// <summary>Sıradan çıxanlar daxil deyil — «neçə dənəmiz var» sualının cavabı
    /// işlək avadanlıqdır, tarixçə deyil.</summary>
    public int Total => Installed + InStock + InRepair;
}

public sealed record CategoryProductVm(
    Guid Id, string Name, string? Sku, int InstalledCount, int SiteCount);

public sealed record CategoryInfoVm(string Name, List<CategoryProductVm> Products);
