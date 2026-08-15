using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Pages;

/// <summary>Kataloqdan gələn kanonik məhsul URL-i: /p/{slug}. QR isə /q/{token}-dən gəlir.</summary>
[OutputCache(PolicyName = "public")]
public sealed class ProductModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IConfiguration _config;

    public ProductModel(AppDbContext db, IFileStorage storage, IConfiguration config)
    {
        _db = db;
        _storage = storage;
        _config = config;
    }

    public PublicProductVm? Product { get; private set; }
    public bool IsArchived { get; private set; }
    public List<PublicProductCardVm> Similar { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var companyId = await PublicCompanyResolver.ResolveAsync(_db, _config);
        if (companyId is null)
            return NotFound();

        var load = await PublicCatalogQueries.LoadProductAsync(
            _db, _storage, companyId.Value, slug: slug);
        if (load is null)
            return NotFound();

        if (load.Product is null)
        {
            // Draft → qonaq üçün mövcud deyil (404); Archived → izahlı səhifə + oxşarlar
            if (load.Status != Domain.Entities.ProductStatus.Archived)
                return NotFound();

            IsArchived = true;
            Similar = await PublicCatalogQueries.SimilarProductsAsync(
                _db, _storage, companyId.Value, load.CategoryId);
            return Page();
        }

        Product = load.Product;
        return Page();
    }
}
