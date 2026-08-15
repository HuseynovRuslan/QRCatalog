using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/settings");

        group.MapGet("/", async (AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            var company = await db.Companies.AsNoTracking()
                .Where(c => c.Id == companyId)
                .Select(c => new SettingsDto(c.Name, c.Phone, c.WhatsappNumber))
                .FirstOrDefaultAsync();
            return company is null ? Results.NotFound() : Results.Ok(company);
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPut("/", async (
            SettingsDto request, AppDbContext db, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            var company = await db.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
            if (company is null) return Results.NotFound();

            try
            {
                company.UpdateProfile(request.Name, request.Phone, request.WhatsappNumber);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanManage)
        .RequireAntiforgery();
    }
}

public sealed record SettingsDto(string Name, string? Phone, string? WhatsappNumber);
