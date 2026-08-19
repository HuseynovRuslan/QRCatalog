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
                    IsDeactivated(user),
                    user.AccessCodeHash is not null));
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

            // Hər yeni işçi HƏM kod, həm parol alır: kod telefonda tək sahə ilə girmək
            // üçün, parol isə kompüterdən e-poçtla girmək istəyənlər üçün.
            var accessCode = GenerateAccessCode();
            user.AccessCodeHash = HashAccessCode(accessCode);

            var tempPassword = GenerateTempPassword();
            var created = await userManager.CreateAsync(user, tempPassword);
            if (!created.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: string.Join(" ", created.Errors.Select(e => Translate(e))));

            await userManager.AddToRoleAsync(user, request.Role);

            // Müvəqqəti parol yalnız BU cavabda görünür — heç yerdə saxlanılmır.
            // Admin onu işçiyə verir, işçi girib dəyişir.
            return Results.Created($"/api/admin/users/{user.Id}",
                new CreatedUserDto(user.Id, user.Email!, tempPassword, accessCode));
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

        // Kod itəndə (telefon itdi, işçi getdi) yenisi verilir — köhnəsi dərhal ölür.
        group.MapPost("/{id:guid}/reset-code", async (
            Guid id, UserManager<ApplicationUser> userManager, ITenantContext tenant) =>
        {
            var (user, error) = await FindScopedAsync(userManager, tenant, id);
            if (user is null) return error!;

            var accessCode = GenerateAccessCode();
            user.AccessCodeHash = HashAccessCode(accessCode);
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: string.Join(" ", updated.Errors.Select(e => Translate(e))));

            // Köhnə kodla açılmış sessiyalar da qapansın — ssenari «telefon itdi»dir
            await userManager.UpdateSecurityStampAsync(user);

            return Results.Ok(new AccessCodeDto(accessCode));
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

    /// <summary>
    /// İşçi kodu: <c>WM-XXXX-XXXX</c>. Əlifbada qarışdırılan simvol yoxdur (0/O, 1/I/L,
    /// 5/S, 2/Z) — kod telefonda diktə olunur və günəşin altında ekrandan oxunur.
    /// 8 simvol × 28 variant ≈ 38 bit: dəqiqədə 10 cəhd limiti ilə kobud güc real deyil.
    /// Defislər yalnız oxunuş üçündür, yoxlamada atılır.
    /// </summary>
    private static string GenerateAccessCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRTUVWXY34689";
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"WM-{new string(chars, 0, 4)}-{new string(chars, 4, 4)}";
    }

    /// <summary>
    /// Kodu normallaşdırıb hash-ləyir: defis, boşluq və registr fərqi nəzərə alınmır —
    /// işçi «wm 4k7p 9x3m» yazsa da girə bilməlidir. Çox qısa girişlər <c>null</c>
    /// qaytarır ki, təsadüfi boş sətir bazada kimləsə uyğun gəlməsin.
    /// </summary>
    internal static string? HashAccessCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        var normalized = new string(code
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (normalized.Length < 8) return null;

        return Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
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
public sealed record UserRowDto(
    Guid Id, string Email, string DisplayName, string Role, bool Deactivated, bool HasCode);
public sealed record CreatedUserDto(Guid Id, string Email, string TempPassword, string AccessCode);
public sealed record TempPasswordDto(string TempPassword);
public sealed record AccessCodeDto(string AccessCode);
