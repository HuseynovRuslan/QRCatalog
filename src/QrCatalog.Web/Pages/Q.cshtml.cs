using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;

namespace QrCatalog.Web.Pages;

public enum QState
{
    NotFound,
    Found,
    Retired,
    Archived,
}

/// <summary>
/// Public QR həlli — `/q/{token}`. Skan edən qonağın heç bir müəssisə konteksti yoxdur,
/// token isə QLOBAL unikaldır; ona görə bu sorğu tenant filtrini QƏSDƏN keçir.
/// Bu, layihədə IgnoreQueryFilters-in icazəli olduğu YEGANƏ public yoldur —
/// cavaba yalnız public sahələr düşür, qiymət/daxili məlumat burada heç sorğulanmır.
/// </summary>
public sealed class QModel : PageModel
{
    private readonly AppDbContext _db;

    public QModel(AppDbContext db) => _db = db;

    public QState State { get; private set; } = QState.NotFound;
    public string? HumanCode { get; private set; }
    public string? TargetName { get; private set; }
    public string? TargetDescription { get; private set; }

    public string Title => State switch
    {
        QState.Found => TargetName ?? "Məhsul",
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
            .IgnoreQueryFilters() // bax: sinif sənədi — public cross-tenant həll nöqtəsi
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.Token == token);

        if (qrCode is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return Page();
        }

        HumanCode = qrCode.HumanCode;

        if (qrCode.Status == QrCodeStatus.Retired)
        {
            State = QState.Retired;
            return Page();
        }

        switch (qrCode.TargetType)
        {
            case QrTargetType.Archive:
                State = QState.Archived;
                return Page();

            case QrTargetType.Category:
                var category = await _db.Categories
                    .IgnoreQueryFilters() // eyni istisna — hədəf başqa cür tapıla bilməz
                    .AsNoTracking()
                    .Where(c => c.Id == qrCode.TargetId && c.CompanyId == qrCode.CompanyId)
                    .Select(c => new { c.Name, c.Description })
                    .FirstOrDefaultAsync();

                if (category is null)
                {
                    State = QState.Archived; // hədəf silinibsə arxiv davranışı — 404 yox
                    return Page();
                }

                State = QState.Found;
                TargetName = category.Name;
                TargetDescription = category.Description;
                return Page();

            case QrTargetType.Product:
                var product = await _db.Products
                    .IgnoreQueryFilters() // eyni istisna — bax: sinif sənədi
                    .AsNoTracking()
                    .Where(p => p.Id == qrCode.TargetId && p.CompanyId == qrCode.CompanyId)
                    .Select(p => new
                    {
                        p.Status,
                        Name = p.Translations
                            .Where(t => t.Lang == "az").Select(t => t.Name).FirstOrDefault(),
                        Description = p.Translations
                            .Where(t => t.Lang == "az").Select(t => t.Description).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync();

                // Draft qonaq üçün hələ mövcud deyil, silinmiş/arxiv "istehsal olunmur" —
                // çap olunmuş etiket heç vaxt 404 görməməlidir
                if (product is null || product.Status != ProductStatus.Published)
                {
                    State = QState.Archived;
                    return Page();
                }

                State = QState.Found;
                TargetName = product.Name;
                TargetDescription = product.Description;
                return Page();

            default:
                State = QState.Archived;
                return Page();
        }
    }
}
