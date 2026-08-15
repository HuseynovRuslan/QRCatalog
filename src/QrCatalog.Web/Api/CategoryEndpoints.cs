using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Application.Common;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class CategoryEndpoints
{
    public static void MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/categories");

        // Düz siyahı qayıdır (parentId + sortOrder ilə) — ağacı client qurur.
        group.MapGet("/", async (AppDbContext db) =>
        {
            var categories = await db.Categories
                .AsNoTracking()
                .OrderBy(c => c.ParentId)
                .ThenBy(c => c.SortOrder)
                .Select(c => new CategoryDto(
                    c.Id, c.ParentId, c.Name, c.Slug, c.Description, c.CodePrefix, c.SortOrder))
                .ToListAsync();
            return Results.Ok(categories);
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPost("/", async (
            CreateCategoryRequest request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Kateqoriya adı boş ola bilməz.");

            if (request.ParentId is { } parentId &&
                !await db.Categories.AnyAsync(c => c.Id == parentId))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Valideyn kateqoriya tapılmadı.");
            }

            var slug = await UniqueSlugAsync(db, SlugGenerator.FromName(request.Name));
            var sortOrder = await NextSortOrderAsync(db, request.ParentId);

            Category category;
            try
            {
                category = Category.Create(companyId, request.Name, slug,
                    request.ParentId, sortOrder, request.Description, request.CodePrefix);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            db.Categories.Add(category);
            await db.SaveChangesAsync();

            return Results.Created($"/api/admin/categories/{category.Id}",
                new CategoryDto(category.Id, category.ParentId, category.Name, category.Slug,
                    category.Description, category.CodePrefix, category.SortOrder));
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateCategoryRequest request, AppDbContext db) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category is null)
                return Results.NotFound();

            try
            {
                category.Update(request.Name, request.Description, request.CodePrefix);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/move", async (
            Guid id, MoveCategoryRequest request, AppDbContext db) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category is null)
                return Results.NotFound();

            if (request.ParentId is { } newParentId)
            {
                if (newParentId == id)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Kateqoriya öz-özünün valideyni ola bilməz.");

                // Dövr qoruması: yeni valideyndən yuxarı qalx — özünə çatsan, alt-budağına köçürülür deməkdir
                var all = await db.Categories.AsNoTracking()
                    .Select(c => new { c.Id, c.ParentId })
                    .ToDictionaryAsync(c => c.Id, c => c.ParentId);

                if (!all.ContainsKey(newParentId))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                        title: "Valideyn kateqoriya tapılmadı.");

                var cursor = (Guid?)newParentId;
                while (cursor is { } current)
                {
                    if (current == id)
                        return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                            title: "Kateqoriya öz alt-kateqoriyasının altına köçürülə bilməz.");
                    cursor = all.GetValueOrDefault(current);
                }
            }

            category.MoveTo(request.ParentId, await NextSortOrderAsync(db, request.ParentId));
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        // Sürüşdürmə bir valideynin altındaki tam sıranı göndərir
        group.MapPut("/reorder", async (ReorderRequest request, AppDbContext db) =>
        {
            var ids = request.OrderedIds;
            var categories = await db.Categories
                .Where(c => ids.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            if (categories.Count != ids.Count)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Sıralamada tanınmayan kateqoriya var.");

            for (var i = 0; i < ids.Count; i++)
                categories[ids[i]].SetSortOrder(i);

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id);
            if (category is null)
                return Results.NotFound();

            if (await db.Categories.AnyAsync(c => c.ParentId == id))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Alt-kateqoriyası olan kateqoriya silinə bilməz — əvvəl onları köçürün.");

            if (await db.Products.AnyAsync(p => p.CategoryId == id))
                return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                    title: "Məhsulu olan kateqoriya silinə bilməz — əvvəl məhsulları başqa kateqoriyaya köçürün.");

            db.Categories.Remove(category);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanManage)
        .RequireAntiforgery();
    }

    private static async Task<string> UniqueSlugAsync(AppDbContext db, string baseSlug)
    {
        // Tenant filtri sorğuya daxildir — unikallıq müəssisə daxilində yoxlanılır
        var slug = baseSlug;
        for (var i = 2; await db.Categories.AnyAsync(c => c.Slug == slug); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }

    private static async Task<int> NextSortOrderAsync(AppDbContext db, Guid? parentId)
    {
        var max = await db.Categories
            .Where(c => c.ParentId == parentId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync();
        return (max ?? -1) + 1;
    }
}

public sealed record CategoryDto(
    Guid Id, Guid? ParentId, string Name, string Slug,
    string? Description, string? CodePrefix, int SortOrder);

public sealed record CreateCategoryRequest(
    string Name, Guid? ParentId, string? Description, string? CodePrefix);

public sealed record UpdateCategoryRequest(string Name, string? Description, string? CodePrefix);

public sealed record MoveCategoryRequest(Guid? ParentId);

public sealed record ReorderRequest(List<Guid> OrderedIds);
