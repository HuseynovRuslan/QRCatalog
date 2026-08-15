using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
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

    public QModel(AppDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public QState State { get; private set; } = QState.NotFound;
    public string? HumanCode { get; private set; }
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

        HumanCode = qrCode.HumanCode;

        if (qrCode.Status == QrCodeStatus.Retired)
        {
            State = QState.Retired;
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
