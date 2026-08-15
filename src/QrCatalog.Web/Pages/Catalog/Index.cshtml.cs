using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Pages.Catalog;

[OutputCache(PolicyName = "public")]
public sealed class IndexModel : PageModel
{
    public const int PageSize = 24;

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IConfiguration _config;

    public IndexModel(AppDbContext db, IFileStorage storage, IConfiguration config)
    {
        _db = db;
        _storage = storage;
        _config = config;
    }

    public bool CatalogAvailable { get; private set; }
    public List<PublicCategoryVm> Categories { get; private set; } = [];
    public List<PublicProductCardVm> Products { get; private set; } = [];
    public int Total { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    [BindProperty(SupportsGet = true, Name = "axtar")]
    public string? Search { get; set; }

    public async Task OnGetAsync(int seh = 1)
    {
        var companyId = await PublicCompanyResolver.ResolveAsync(_db, _config);
        if (companyId is null)
        {
            CatalogAvailable = false;
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        CatalogAvailable = true;
        CurrentPage = Math.Max(1, seh);

        Categories = await PublicCatalogQueries.CategoriesAsync(_db, companyId.Value, parentId: null);
        (Products, Total) = await PublicCatalogQueries.ProductCardsAsync(
            _db, _storage, companyId.Value,
            categoryId: null, search: Search, page: CurrentPage, pageSize: PageSize);
    }
}
