using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Infrastructure.Scans;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Pages;

public enum QState
{
    NotFound,
    Found,
    Retired,
    Archived,
}

/// <summary>
/// Public QR həlli — `/q/{token}`. Skan edən qonağın müəssisə konteksti yoxdur,
/// token isə QLOBAL unikaldır; ona görə buradakı sorğular tenant filtrini QƏSDƏN keçir
/// (bax: PublicCatalogQueries sənədi — icazəli iki yerdən biri).
/// Cavaba yalnız public sahələr düşür — qiymət və daxili məlumat strukturda yoxdur.
/// </summary>
[EnableRateLimiting("qr-resolve")]
[OutputCache(PolicyName = "public")]
public sealed class QModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ScanEventQueue _scans;

    public QModel(AppDbContext db, IFileStorage storage, ScanEventQueue scans)
    {
        _db = db;
        _storage = storage;
        _scans = scans;
    }

    public QState State { get; private set; } = QState.NotFound;
    public string? HumanCode { get; private set; }

    /// <summary>Skan beacon-u üçün — kod tapılanda dolur, tapılmayanda boş qalır.</summary>
    public string? Token { get; private set; }
    public PublicProductVm? Product { get; private set; }
    public List<PublicProductCardVm> Similar { get; private set; } = [];

    public string Title => State switch
    {
        QState.Found => Product?.Name ?? "Məhsul",
        QState.Retired => "Kod istifadədə deyil",
        QState.Archived => "Model istehsal olunmur",
        _ => "Kod tapılmadı",
    };

    public async Task<IActionResult> OnGetAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 24)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        var qrCode = await _db.QrCodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Token == token);

        if (qrCode is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        // İŞÇİ SKANI: girişli istifadəçi müştəri səhifəsi yerinə admin ekranına yönləndirilir —
        // eyni etiket iki auditoriyaya iki fərqli ekran açır. Keş bunu POZA BİLMƏZ:
        // output cache-in default siyasəti girişli sorğular üçün nə saxlayır, nə keşdən verir
        // (üstəlik 302 onsuz da keşlənmir), yəni qonağın keşlənmiş 200-ü admin-ə düşmür.
        //
        // Yalnız ÖZ müəssisəsinin kodunda: SaaS-da B şirkətinin admini A şirkətinin etiketini
        // skan etsə adi müştəri kimi məhsul səhifəsini görür — daxili ekran açılmır.
        //
        // Şərt qəsdən skan qeydindən ƏVVƏLDİR: işçinin öz yoxlama skanları müştəri
        // statistikasına düşməməlidir, yoxsa "ən çox maraq görən model" siyahısı
        // briqadanın marşrutunu göstərər.
        if (User.Identity?.IsAuthenticated == true &&
            Guid.TryParse(User.FindFirst(AppClaims.CompanyId)?.Value, out var staffCompany) &&
            staffCompany == qrCode.CompanyId)
        {
            // Hədəf REDAKTOR deyil, məlumat ekranıdır: sahədə duran adamın sualı
            // «bu nədir, neçə dənəmiz var, hara qoymuşuq»dur. Redaktəyə keçid o
            // səhifədə düymədir — icazəsi olana görünür.
            return Redirect($"/i/{qrCode.Token}");
        }

        HumanCode = qrCode.HumanCode;
        Token = qrCode.Token; // skan qeydi beacon ilə gedir — retired/arxiv halları da sayılır

        if (qrCode.Status == QrCodeStatus.Retired)
        {
            State = QState.Retired;
            return Page();
        }

        // Vahid kodu qonaq üçün MƏHSUL səhifəsidir: obyekt adı, ünvan, nüsxə kodu və
        // anbar qalığı daxili məlumatdır — müştəriyə getmir. İşçi isə yuxarıda
        // /i/{token}-ə yönləndirilib və orada tam kartı görür.
        if (qrCode.TargetType == QrTargetType.Unit && qrCode.TargetId is { } unitId)
        {
            // IgnoreQueryFilters açıqdır — CompanyId şərti MÜTLƏQ əl ilə yazılmalıdır,
            // yoxsa başqa müəssisənin nüsxəsi tapıla bilər.
            var unitProductId = await _db.SiteUnits.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == unitId && u.CompanyId == qrCode.CompanyId)
                .Select(u => (Guid?)u.ProductId)
                .FirstOrDefaultAsync();

            if (unitProductId is { } pid)
            {
                var unitLoad = await PublicCatalogQueries.LoadProductAsync(
                    _db, _storage, qrCode.CompanyId, id: pid,
                    humanCode: qrCode.HumanCode, qrToken: qrCode.Token);

                if (unitLoad?.Product is { } unitProduct)
                {
                    State = QState.Found;
                    Product = unitProduct;
                    return Page();
                }

                // Məhsul qaralama/arxivdədir: çap olunmuş etiket 404 GÖRMƏMƏLİDİR
                State = QState.Archived;
                if (unitLoad is not null)
                    Similar = await PublicCatalogQueries.SimilarProductsAsync(
                        _db, _storage, qrCode.CompanyId, unitLoad.CategoryId, pid);
                return Page();
            }

            // Nüsxə silinib — etiket kağızda qalır, izahlı səhifə göstərilir
            State = QState.Archived;
            return Page();
        }

        switch (qrCode.TargetType)
        {
            case QrTargetType.Category:
                var categorySlug = await _db.Categories.IgnoreQueryFilters().AsNoTracking()
                    .Where(c => c.Id == qrCode.TargetId && c.CompanyId == qrCode.CompanyId)
                    .Select(c => c.Slug)
                    .FirstOrDefaultAsync();
                if (categorySlug is null)
                {
                    State = QState.Archived;
                    return Page();
                }

                // Skan BURADA qeyd olunur, beacon ilə yox: qonaq kataloq səhifəsinə keçir və
                // orada token daşınmır. 302 cavabı output-cache-lənmədiyi üçün (yalnız 200
                // keşlənir) bu qol hər skanda işləyir — sayğac tam olur. Bax: ScanRecorder.
                ScanRecorder.Record(_scans, HttpContext, qrCode.CompanyId, qrCode.Id);
                return Redirect($"/katalog/{categorySlug}");

            case QrTargetType.Product:
                var load = await PublicCatalogQueries.LoadProductAsync(
                    _db, _storage, qrCode.CompanyId, id: qrCode.TargetId,
                    humanCode: qrCode.HumanCode, qrToken: qrCode.Token);

                if (load?.Product is { } product)
                {
                    State = QState.Found;
                    Product = product;
                    return Page();
                }

                // Draft/Arxiv/silinmiş — çap olunmuş etiket heç vaxt 404 görməməlidir
                State = QState.Archived;
                if (load is not null)
                    Similar = await PublicCatalogQueries.SimilarProductsAsync(
                        _db, _storage, qrCode.CompanyId, load.CategoryId, qrCode.TargetId);
                return Page();

            case QrTargetType.Archive:
            default:
                State = QState.Archived;
                return Page();
        }
    }
}
