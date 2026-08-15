using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class InquiryEndpoints
{
    public static void MapInquiryEndpoints(this IEndpointRouteBuilder app)
    {
        MapPublicEndpoint(app);
        MapAdminEndpoints(app);
    }

    private static void MapPublicEndpoint(IEndpointRouteBuilder app)
    {
        // Anonim public endpoint: auth cookie işlətmir, ona görə antiforgery mənasızdır —
        // qorunma spam-a qarşıdır: honeypot + IP rate limit. Sorğu tenant filtrini keçmir,
        // company mənbədən (QR/məhsul) tapılır.
        app.MapPost("/api/public/inquiries", async (
            PublicInquiryRequest request, AppDbContext db, IEmailSender email,
            IServiceScopeFactory scopeFactory, IConfiguration config,
            ILoggerFactory loggerFactory) =>
        {
            // Honeypot: gizli sahəni yalnız botlar doldurur. Bota uğur kimi görünsün.
            if (!string.IsNullOrEmpty(request.Website))
                return Results.NoContent();

            // Mənbədən müəssisəni tap
            Guid? companyId = null;
            Guid? productId = null;
            Guid? qrCodeId = null;

            if (!string.IsNullOrWhiteSpace(request.QrToken))
            {
                var qr = await db.QrCodes.IgnoreQueryFilters().AsNoTracking()
                    .Where(q => q.Token == request.QrToken)
                    .Select(q => new { q.Id, q.CompanyId, q.TargetId, q.TargetType })
                    .FirstOrDefaultAsync();
                if (qr is not null)
                {
                    companyId = qr.CompanyId;
                    qrCodeId = qr.Id;
                    if (qr.TargetType == QrTargetType.Product)
                        productId = qr.TargetId;
                }
            }

            if (companyId is null && !string.IsNullOrWhiteSpace(request.ProductSlug))
            {
                var product = await db.Products.IgnoreQueryFilters().AsNoTracking()
                    .Where(p => p.Slug == request.ProductSlug)
                    .Select(p => new { p.Id, p.CompanyId })
                    .FirstOrDefaultAsync();
                if (product is not null)
                {
                    companyId = product.CompanyId;
                    productId ??= product.Id;
                }
            }

            companyId ??= await PublicCompanyResolver.ResolveAsync(db, config);
            if (companyId is null)
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "Müraciət qəbul edilə bilmədi.");

            Inquiry inquiry;
            try
            {
                inquiry = Inquiry.Create(companyId.Value, productId, qrCodeId,
                    request.Name, request.Phone, request.Message);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
            }

            db.Inquiries.Add(inquiry);
            await db.SaveChangesAsync();

            // Bildiriş fonda — SMTP yavaşlığı qonağın cavabını bloklamasın
            NotifyAdminsInBackground(scopeFactory, loggerFactory, inquiry.Id);

            return Results.NoContent();
        })
        .RequireRateLimiting("inquiry");
    }

    private static void NotifyAdminsInBackground(
        IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory, Guid inquiryId)
    {
        var logger = loggerFactory.CreateLogger("InquiryNotify");
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                var inquiry = await db.Inquiries.IgnoreQueryFilters().AsNoTracking()
                    .FirstAsync(i => i.Id == inquiryId);

                var productName = inquiry.ProductId is null
                    ? null
                    : await db.Products.IgnoreQueryFilters().AsNoTracking()
                        .Where(p => p.Id == inquiry.ProductId)
                        .SelectMany(p => p.Translations)
                        .Where(t => t.Lang == "az")
                        .Select(t => t.Name)
                        .FirstOrDefaultAsync();

                var userManager = scope.ServiceProvider
                    .GetRequiredService<UserManager<ApplicationUser>>();
                var admins = await userManager.GetUsersInRoleAsync(AppRoles.Admin);
                var recipients = admins
                    .Where(u => u.CompanyId == inquiry.CompanyId && u.Email is not null)
                    .Select(u => u.Email!)
                    .ToList();

                await email.SendAsync(recipients,
                    subject: productName is null
                        ? "QrCatalog: yeni müraciət"
                        : $"QrCatalog: yeni sorğu — {productName}",
                    textBody:
                        $"Ad: {inquiry.Name}\n" +
                        $"Telefon: {inquiry.Phone}\n" +
                        (productName is null ? "" : $"Məhsul: {productName}\n") +
                        (inquiry.Message is null ? "" : $"\n{inquiry.Message}\n") +
                        "\nAdmin panel: /admin/muracietler");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sorğu bildirişi göndərilə bilmədi ({InquiryId})", inquiryId);
            }
        });
    }

    private static void MapAdminEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/inquiries");

        group.MapGet("/", async (AppDbContext db, string? status, int page = 1, int pageSize = 50) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.Inquiries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status) &&
                Enum.TryParse<InquiryStatus>(status, ignoreCase: true, out var parsed))
                query = query.Where(i => i.Status == parsed);

            var total = await query.CountAsync();
            var rows = await query
                .OrderByDescending(i => i.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var productIds = rows.Where(r => r.ProductId != null)
                .Select(r => r.ProductId!.Value).Distinct().ToList();
            var productNames = productIds.Count == 0
                ? []
                : await db.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new
                    {
                        p.Id,
                        Name = p.Translations.Where(t => t.Lang == "az")
                            .Select(t => t.Name).FirstOrDefault() ?? "(adsız)",
                    })
                    .ToDictionaryAsync(p => p.Id, p => p.Name);

            var qrIds = rows.Where(r => r.QrCodeId != null)
                .Select(r => r.QrCodeId!.Value).Distinct().ToList();
            var qrCodes = qrIds.Count == 0
                ? []
                : await db.QrCodes.AsNoTracking()
                    .Where(q => qrIds.Contains(q.Id))
                    .ToDictionaryAsync(q => q.Id, q => q.HumanCode);

            var items = rows.Select(i => new InquiryDto(
                i.Id, i.Name, i.Phone, i.Message,
                i.Status.ToString(), i.InternalNote,
                i.ProductId is { } pid ? productNames.GetValueOrDefault(pid) : null,
                i.QrCodeId is { } qid ? qrCodes.GetValueOrDefault(qid) : null,
                i.CreatedAtUtc)).ToList();

            return Results.Ok(new PagedResult<InquiryDto>(items, total, page, pageSize));
        })
        .RequireAuthorization(Policies.CanView);

        group.MapPut("/{id:guid}/status", async (
            Guid id, SetStatusRequest request, AppDbContext db) =>
        {
            if (!Enum.TryParse<InquiryStatus>(request.Status, ignoreCase: true, out var status))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Status tanınmadı.");

            var inquiry = await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id);
            if (inquiry is null) return Results.NotFound();

            inquiry.SetStatus(status);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/note", async (
            Guid id, SetNoteRequest request, AppDbContext db) =>
        {
            var inquiry = await db.Inquiries.FirstOrDefaultAsync(i => i.Id == id);
            if (inquiry is null) return Results.NotFound();

            inquiry.SetNote(request.Note);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .RequireAuthorization(Policies.CanEdit)
        .RequireAntiforgery();
    }
}

public sealed record PublicInquiryRequest(
    string Name, string Phone, string? Message,
    string? ProductSlug, string? QrToken,
    string? Website); // honeypot — insanlar bunu görmür, botlar doldurur

public sealed record InquiryDto(
    Guid Id, string Name, string Phone, string? Message,
    string Status, string? InternalNote,
    string? ProductName, string? HumanCode, DateTime CreatedAtUtc);

public sealed record SetStatusRequest(string Status);

public sealed record SetNoteRequest(string? Note);
