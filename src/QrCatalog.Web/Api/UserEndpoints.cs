using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

/// <summary>
/// İstifadəçi idarəetməsi — hər işçinin ÖZ hesabı olsun deyə. Paylaşılan parol audit
/// jurnalını mənasızlaşdırır (hər sətir eyni adamı göstərir) və işçi gedəndə hamının
/// parolunu dəyişməyə məcbur edir.
///
/// SMTP hələ konfiqurasiya olunmadığı üçün "parolu unutdum" axını YOXDUR: admin müvəqqəti
/// parol yaradır, cavabda BİR DƏFƏ görünür, işçi girib özü dəyişir (/api/auth/change-password).
///
/// Diqqət: ApplicationUser Identity cədvəlidir, tenant filtri ona avtomatik QOŞULMUR —
/// hər sorğu CompanyId ilə açıq şəkildə məhdudlaşdırılır və tenant yoxdursa 403 qayıdır.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users")
            .RequireAuthorization(Policies.CanManage);

        group.MapGet("/", async (
            UserManager<ApplicationUser> userManager, ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");

            var users = await userManager.Users.AsNoTracking()
                .Where(u => u.CompanyId == companyId)
                .OrderBy(u => u.Email)
                .ToListAsync();

            var items = new List<UserRowDto>(users.Count);
            foreach (var user in users)
            {
                var roles = await userManager.GetRolesAsync(user);
                items.Add(new UserRowDto(
                    user.Id, user.Email ?? "", user.DisplayName,
                    roles.FirstOrDefault() ?? "—",
                    IsDeactivated(user)));
            }

            return Results.Ok(items);
        });

        group.MapPost("/", async (
            CreateUserRequest request,
            UserManager<ApplicationUser> userManager,
            ITenantContext tenant) =>
        {
            if (tenant.CompanyId is not { } companyId)
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Bu əməliyyat üçün müəssisə konteksti lazımdır.");
            if (!AppRoles.All.Contains(request.Role))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Rol tanınmadı.");

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName?.Trim() ?? "",
                CompanyId = companyId,
                // Deaktiv etmə mexanizmi lockout üzərində qurulub — açıq olmalıdır
                LockoutEnabled = true,
            };

            var tempPassword = GenerateTempPassword();
            var created = await userManager.CreateAsync(user, tempPassword);
            if (!created.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: string.Join(" ", created.Errors.Select(e => Translate(e))));

            await userManager.AddToRoleAsync(user, request.Role);

            // Müvəqqəti parol yalnız BU cavabda görünür — heç yerdə saxlanılmır.
            // Admin onu işçiyə verir, işçi girib dəyişir.
            return Results.Created($"/api/admin/users/{user.Id}",
                new CreatedUserDto(user.Id, user.Email!, tempPassword));
        })
        .RequireAntiforgery();

        group.MapPut("/{id:guid}/role", async (
            Guid id, SetRoleRequest request,
            UserManager<ApplicationUser> userManager,
            ITenantContext tenant, ClaimsPrincipal principal) =>
        {
            if (!AppRoles.All.Contains(request.Role))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Rol tanınmadı.");

            var (user, error) = await FindScopedAsync(userManager, tenant, id);
            if (user is null) return error!;

            // Özünü Admin-likdən salmaq olmaz: son admin qəza ilə itə bilər və
            // sistemi idarə edən heç kim qalmaz
            if (IsSelf(principal, user) && request.Role != AppRoles.Admin)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Öz rolunuzu aşağı sala bilməzsiniz — başqa admin etsin.");

            var current = await userManager.GetRolesAsync(user);
            await userManager.RemoveFromRolesAsync(user, current);
            await userManager.AddToRoleAsync(user, request.Role);
            // Rol cookie-də daşınır — köhnə sessiya köhnə səlahiyyətlə qalmasın
            await userManager.UpdateSecurityStampAsync(user);

            return Results.NoContent();
        })
        .RequireAntiforgery();

        group.MapPost("/{id:guid}/deactivate", async (
            Guid id, UserManager<ApplicationUser> userManager,
            ITenantContext tenant, ClaimsPrincipal principal) =>
        {
            var (user, error) = await FindScopedAsync(userManager, tenant, id);
            if (user is null) return error!;

            if (IsSelf(principal, user))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Öz hesabınızı deaktiv edə bilməzsiniz.");

            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
            // Mövcud cookie-lər security stamp yoxlamasında (5 dəq) etibarsızlaşır
            await userManager.UpdateSecurityStampAsync(user);

            return Results.NoContent();
        })
        .RequireAntiforgery();

        group.MapPost("/{id:guid}/activate", async (
            Guid id, UserManager<ApplicationUser> userManager, ITenantContext tenant) =>
        {
            var (user, error) = await FindScopedAsync(userManager, tenant, id);
            if (user is null) return error!;

            await userManager.SetLockoutEndDateAsync(user, null);
            return Results.NoContent();
        })
        .RequireAntiforgery();

        group.MapPost("/{id:guid}/reset-password", async (
            Guid id, UserManager<ApplicationUser> userManager, ITenantContext tenant) =>
        {
            var (user, error) = await FindScopedAsync(userManager, tenant, id);
            if (user is null) return error!;

            var tempPassword = GenerateTempPassword();
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await userManager.ResetPasswordAsync(user, token, tempPassword);
            if (!reset.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: string.Join(" ", reset.Errors.Select(e => Translate(e))));

            // Parolu sıfırlayan adam köhnə sessiyaları da öldürmək istəyir
            // (ssenari adətən "telefon itdi"dir)
            await userManager.UpdateSecurityStampAsync(user);

            return Results.Ok(new TempPasswordDto(tempPassword));
        })
        .RequireAntiforgery();
    }

    /// <summary>İstifadəçini TAPIB tenant sərhədini yoxlayır — başqa müəssisənin
    /// istifadəçisi bu API-dən görünməz olmalıdır (404, 403 yox: mövcudluq sızmasın).</summary>
    private static async Task<(ApplicationUser?, IResult?)> FindScopedAsync(
        UserManager<ApplicationUser> userManager, ITenantContext tenant, Guid id)
    {
        if (tenant.CompanyId is not { } companyId)
            return (null, Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Bu əməliyyat üçün müəssisə konteksti lazımdır."));

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null || user.CompanyId != companyId)
            return (null, Results.NotFound());

        return (user, null);
    }

    private static bool IsSelf(ClaimsPrincipal principal, ApplicationUser user) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var selfId) &&
        selfId == user.Id;

    /// <summary>Deaktiv = lockout uzaq gələcəyə qoyulub. Adi brute-force kilidi
    /// (5 dəqiqəlik) deaktiv sayılmır.</summary>
    internal static bool IsDeactivated(ApplicationUser user) =>
        user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(1);

    /// <summary>
    /// Oxunaqlı müvəqqəti parol: qarışdırılan simvollar (0/O, 1/l/I) yoxdur — admin onu
    /// işçiyə telefonda DİKTƏ edəcək. Sonluq parol siyasətinin hərf/rəqəm tələbini təmin edir.
    /// </summary>
    private static string GenerateTempPassword()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
        var body = new char[10];
        for (var i = 0; i < body.Length; i++)
            body[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(body) + "Wm" + RandomNumberGenerator.GetInt32(2, 10);
    }

    private static string Translate(IdentityError error) => error.Code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => "Bu e-poçt artıq qeydiyyatdadır.",
        "InvalidEmail" or "InvalidUserName" => "E-poçt düzgün deyil.",
        _ => error.Description,
    };
}

public sealed record CreateUserRequest(string Email, string? DisplayName, string Role);
public sealed record SetRoleRequest(string Role);
public sealed record UserRowDto(Guid Id, string Email, string DisplayName, string Role, bool Deactivated);
public sealed record CreatedUserDto(Guid Id, string Email, string TempPassword);
public sealed record TempPasswordDto(string TempPassword);
