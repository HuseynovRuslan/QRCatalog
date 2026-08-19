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

    /// <summary>
    /// Etiket istifadədən çıxarılıb. İşçi bunu GÖRMƏLİDİR: müştəri həmin kodu skan
    /// edəndə boş səhifə alır, etiketi tapıb dəyişməli olan yeganə adam isə işçidir.
    /// Xəbərdarlıq olmasa ölü etiket yerində qalır və heç kim səbəbini bilmir.
    /// </summary>
    public bool Retired { get; private set; }

    // Vahid kodu üçün — konkret fiziki nüsxə
    public UnitInfoVm? Unit { get; private set; }

    // Məhsul kodu üçün
    public ProductInfoVm? Product { get; private set; }

    // Kateqoriya kodu üçün
    public CategoryInfoVm? Category { get; private set; }

    public string Title => Unit?.UnitCode ?? Product?.Name ?? Category?.Name ?? "Məlumat";

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
        Retired = qrCode.Status == QrCodeStatus.Retired;
        CanEdit = User.IsInRole(AppRoles.Admin) || User.IsInRole(AppRoles.Editor);

        return qrCode.TargetType switch
        {
            QrTargetType.Unit when qrCode.TargetId is { } unitId =>
                await LoadUnitAsync(companyId, unitId),
            QrTargetType.Product when qrCode.TargetId is { } productId =>
                await LoadProductAsync(companyId, productId),
            QrTargetType.Category when qrCode.TargetId is { } categoryId =>
                await LoadCategoryAsync(companyId, categoryId),
            _ => Redirect("/admin/qr"),
        };
    }

    /// <summary>
    /// NÜSXƏ EKRANI — «bu skamya haradadır» sualının cavabı.
    ///
    /// Sıralama qəsdən belədir: kimlik → YER (obyekt, ünvan, koordinat) → vəziyyət və
    /// zəmanət → əlaqə. Sahədə duran adamın ilk sualı yerdir, say deyil; saylar bir
    /// toxunuş arxasındadır. Model səviyyəli ekranda əvvəl iri rəqəm gəlirdi — o,
    /// rəhbərlik üçün düzgündür, sahə üçün yox.
    /// </summary>
    private async Task<IActionResult> LoadUnitAsync(Guid companyId, Guid unitId)
    {
        var unit = await _db.SiteUnits
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.Id == unitId && u.CompanyId == companyId)
            .Select(u => new
            {
                u.Id,
                u.Code,
                u.Status,
                u.InstalledOn,
                u.Note,
                u.Latitude,
                u.Longitude,
                u.ProductId,
                u.SiteId,
                ProductName = _db.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == u.ProductId)
                    .SelectMany(p => p.Translations.Where(t => t.Lang == "az").Select(t => t.Name))
                    .FirstOrDefault(),
                ProductSku = _db.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == u.ProductId).Select(p => p.Sku).FirstOrDefault(),
                ImagePrefix = _db.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == u.ProductId)
                    .SelectMany(p => p.Images.OrderBy(i => i.SortOrder).Select(i => i.StoragePrefix))
                    .FirstOrDefault(),
                CategoryId = _db.Products.IgnoreQueryFilters()
                    .Where(p => p.Id == u.ProductId).Select(p => (Guid?)p.CategoryId).FirstOrDefault(),
            })
            .FirstOrDefaultAsync();

        if (unit is null)
            return NotFound();

        var site = unit.SiteId is null ? null : await _db.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == unit.SiteId && x.CompanyId == companyId)
            .Select(x => new
            {
                x.Id, x.Name, x.Kind, x.Address, x.Latitude, x.Longitude,
                x.ContactName, x.ContactPhone,
            })
            .FirstOrDefaultAsync();

        // Zəmanət müddəti spesifikasiyadadır («Zəmanət» → «2 il»). Sahədə ən bahalı
        // qərar məhz budur: təmir pulsuzdur, yoxsa ödənişli.
        var specs = await _db.Products
            .IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == unit.ProductId)
            .SelectMany(p => p.Specs.Select(sp => new { sp.Label, sp.Value }))
            .ToListAsync();
        var warrantyText = specs
            .FirstOrDefault(sp => sp.Label.StartsWith("Zəmanət", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var siblings = await _db.SiteUnits
            .IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.ProductId == unit.ProductId && u.CompanyId == companyId)
            .Select(u => new { u.Id, u.Code, u.Status, u.SiteId })
            .ToListAsync();

        var sameSite = siblings
            .Where(u => u.SiteId == unit.SiteId && u.Id != unit.Id &&
                        u.Status == UnitStatus.Installed)
            .OrderBy(u => u.Code)
            .Select(u => u.Code)
            .ToList();

        // Obyektdəki DİGƏR modellərimiz — xidmət marşrutu üçün: adam onsuz da oradadır.
        //
        // İKİ addımda: qruplaşdırma ilə eyni sorğuda ad alt-sorğusu qoymaq EF-in
        // tərcümə edə bilmədiyi ifadə verir (GroupBy + korrelyasiyalı alt-sorğu) —
        // nəticə 500 olur və səbəb yalnız server jurnalında görünür.
        var siteOther = new List<SiteProductVm>();
        if (unit.SiteId is not null)
        {
            var grouped = await _db.SiteUnits
                .IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.SiteId == unit.SiteId && u.CompanyId == companyId &&
                            u.ProductId != unit.ProductId && u.Status == UnitStatus.Installed)
                .GroupBy(u => u.ProductId)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToListAsync();

            if (grouped.Count > 0)
            {
                var otherIds = grouped.Select(g => g.ProductId).ToList();
                var otherNames = await _db.Products
                    .IgnoreQueryFilters().AsNoTracking()
                    .Where(p => otherIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        Name = p.Translations.Where(t => t.Lang == "az")
                            .Select(t => t.Name).FirstOrDefault(),
                    })
                    .ToDictionaryAsync(p => p.Id, p => p.Name ?? "(adsız)");

                siteOther = grouped
                    .Select(g => new SiteProductVm(
                        otherNames.GetValueOrDefault(g.ProductId, "(adsız)"), g.Count))
                    .OrderByDescending(x => x.Count)
                    .ToList();
            }
        }

        Unit = new UnitInfoVm(
            unit.Id,
            unit.Code,
            unit.ProductId,
            unit.ProductName ?? "Adsız məhsul",
            unit.ProductSku,
            unit.ImagePrefix is null
                ? null
                : _storage.GetPublicUrl($"{unit.ImagePrefix}/w320.webp"),
            UnitStatusLabel(unit.Status),
            unit.Status,
            unit.InstalledOn,
            WarrantyEndsOn(unit.InstalledOn, warrantyText),
            unit.Note,
            site is null ? null : new UnitSiteVm(
                site.Id, site.Name, SiteKindLabel(site.Kind), site.Address,
                unit.Latitude ?? site.Latitude,
                unit.Longitude ?? site.Longitude,
                ExactPosition: unit.Latitude is not null,
                site.ContactName, site.ContactPhone),
            Installed: siblings.Count(u => u.Status == UnitStatus.Installed),
            InStock: siblings.Count(u => u.Status == UnitStatus.InStock),
            InRepair: siblings.Count(u => u.Status == UnitStatus.InRepair),
            SameSiteCodes: sameSite,
            SiteOtherProducts: siteOther);

        return Page();
    }

    /// <summary>«2 il» + quraşdırma tarixi → bitmə tarixi. Format tanınmasa null.</summary>
    private static DateOnly? WarrantyEndsOn(DateOnly? installedOn, string? warranty)
    {
        if (installedOn is not { } start || string.IsNullOrWhiteSpace(warranty))
            return null;

        var digits = new string(warranty.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(digits, out var amount) || amount <= 0)
            return null;

        return warranty.Contains("ay", StringComparison.OrdinalIgnoreCase)
            ? start.AddMonths(amount)
            : start.AddYears(amount);
    }

    private static string UnitStatusLabel(UnitStatus status) => status switch
    {
        UnitStatus.Installed => "Quraşdırılıb",
        UnitStatus.InStock => "Anbarda",
        UnitStatus.InRepair => "Təmirdə",
        _ => "Çıxarılıb",
    };

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

public sealed record UnitSiteVm(
    Guid SiteId,
    string Name,
    string Kind,
    string? Address,
    double Latitude,
    double Longitude,
    /// <summary>Nüsxənin öz koordinatı var, yoxsa obyektin ümumi nöqtəsi işlədilir.</summary>
    bool ExactPosition,
    string? ContactName,
    string? ContactPhone);

public sealed record SiteProductVm(string Name, int Count);

public sealed record UnitInfoVm(
    Guid Id,
    string UnitCode,
    Guid ProductId,
    string ProductName,
    string? Sku,
    string? ImageUrl,
    string StatusLabel,
    UnitStatus Status,
    DateOnly? InstalledOn,
    DateOnly? WarrantyEndsOn,
    string? Note,
    UnitSiteVm? Site,
    int Installed,
    int InStock,
    int InRepair,
    List<string> SameSiteCodes,
    List<SiteProductVm> SiteOtherProducts)
{
    public int Total => Installed + InStock + InRepair;

    /// <summary>Quraşdırmadan keçən müddət — «1 il 3 ay» kimi.</summary>
    public string? Age
    {
        get
        {
            if (InstalledOn is not { } start) return null;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (start > today) return null;

            var months = (today.Year - start.Year) * 12 + today.Month - start.Month;
            if (today.Day < start.Day) months--;
            if (months < 1) return "bu ay";

            var years = months / 12;
            var rest = months % 12;
            return years == 0 ? $"{rest} ay"
                : rest == 0 ? $"{years} il"
                : $"{years} il {rest} ay";
        }
    }

    /// <summary>Zəmanətin qalan müddəti; bitibsə mənfi işarəli mətn.</summary>
    public (string Text, bool Expired)? Warranty
    {
        get
        {
            if (WarrantyEndsOn is not { } ends) return null;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (ends < today) return ($"{ends:dd.MM.yyyy} — bitib", true);

            var months = (ends.Year - today.Year) * 12 + ends.Month - today.Month;
            var left = months < 1 ? "bu ay bitir"
                : months < 12 ? $"{months} ay qalıb"
                : $"{months / 12} il {months % 12} ay qalıb";
            return ($"{ends:dd.MM.yyyy} — {left}", false);
        }
    }
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
