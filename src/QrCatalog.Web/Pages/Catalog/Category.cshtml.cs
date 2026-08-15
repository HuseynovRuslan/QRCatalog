using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Pages.Catalog;

[OutputCache(PolicyName = "public")]
public sealed class CategoryModel : PageModel
{
    public const int PageSize = 24;

    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly IConfiguration _config;

    public CategoryModel(AppDbContext db, IFileStorage storage, IConfiguration config)
    {
        _db = db;
        _storage = storage;
        _config = config;
    }

    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    public List<BreadcrumbVm> Breadcrumbs { get; private set; } = [];
    public List<PublicCategoryVm> Children { get; private set; } = [];
    public List<PublicProductCardVm> Products { get; private set; } = [];
    public int Total { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public string Slug { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string slug, int seh = 1)
    {
        var companyId = await PublicCompanyResolver.ResolveAsync(_db, _config);
        if (companyId is null)
            return NotFound();

        var category = await _db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Slug == slug)
            .Select(c => new { c.Id, c.ParentId, c.Name, c.Description, c.Slug })
            .FirstOrDefaultAsync();
        if (category is null)
            return NotFound();

        Name = category.Name;
        Description = category.Description;
        Slug = category.Slug;
        CurrentPage = Math.Max(1, seh);

        // Breadcrumb zənciri — yuxarı qalx
        var all = await _db.Categories.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == companyId)
            .Select(c => new { c.Id, c.ParentId, c.Name, c.Slug })
            .ToDictionaryAsync(c => c.Id);
        var chain = new List<BreadcrumbVm> { new(category.Name, null) };
        var cursor = category.ParentId;
        while (cursor is { } parentId && all.TryGetValue(parentId, out var parent))
        {
            chain.Add(new BreadcrumbVm(parent.Name, $"/katalog/{parent.Slug}"));
            cursor = parent.ParentId;
        }
        chain.Add(new BreadcrumbVm("Kataloq", "/katalog"));
        chain.Reverse();
        Breadcrumbs = chain;

        Children = await PublicCatalogQueries.CategoriesAsync(_db, companyId.Value, category.Id);
        (Products, Total) = await PublicCatalogQueries.ProductCardsAsync(
            _db, _storage, companyId.Value,
            categoryId: category.Id, search: null, page: CurrentPage, pageSize: PageSize);

        return Page();
    }
}
