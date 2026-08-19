using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Web.Infrastructure;

namespace QrCatalog.Web.Api;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        // SPA əvvəlcə buradan token alır, sonra hər yazma sorğusunda X-XSRF-TOKEN başlığında göndərir
        group.MapGet("/antiforgery", (IAntiforgery antiforgery, HttpContext context) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        group.MapPost("/login", async (
            LoginRequest request,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "E-poçt və ya parol yanlışdır.");

            // Deaktiv hesab lockout üzərində qurulub — amma istifadəçiyə "5 dəqiqəlik
            // kilid" demək yalan olardı, açıq mesaj verilir
            if (UserEndpoints.IsDeactivated(user))
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                    title: "Hesab deaktiv edilib — rəhbərliklə əlaqə saxlayın.");

            var check = await signInManager.CheckPasswordSignInAsync(
                user, request.Password, lockoutOnFailure: true);

            if (check.IsLockedOut)
                return Results.Problem(statusCode: StatusCodes.Status423Locked,
                    title: "Çox sayda uğursuz cəhd — hesab 5 dəqiqəlik kilidləndi.");
            if (!check.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "E-poçt və ya parol yanlışdır.");

            // "Məni xatırla": sahə işçisi telefonda 30 gün parol görmür. İşarələnməyibsə
            // sessiya cookie-si (brauzer bağlananda gedir), bilet 8 saat yaşayır —
            // ofis kompüteri üçün düzgün davranış.
            await signInManager.SignInAsync(user, new AuthenticationProperties
            {
                IsPersistent = request.RememberMe,
                ExpiresUtc = request.RememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null,
            });

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new UserInfo(user.Email!, user.DisplayName, roles, user.CompanyId));
        })
        .RequireRateLimiting("login")
        .RequireAntiforgery();

        // İşçi müvəqqəti parolla girib özü dəyişir — SMTP olmadığı üçün "unutdum" axını
        // yoxdur, bu isə minimum gigiyenadır: admin işçinin daimi parolunu BİLMƏMƏLİDİR.
        group.MapPost("/change-password", async (
            ChangePasswordRequest request,
            ClaimsPrincipal principal,
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal);
            if (user is null)
                return Results.Unauthorized();

            var result = await userManager.ChangePasswordAsync(
                user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: result.Errors.Any(e => e.Code == "PasswordMismatch")
                        ? "Hazırkı parol yanlışdır."
                        : "Yeni parol tələblərə uymur: ən azı 8 simvol, böyük və kiçik hərf, rəqəm.");

            // ChangePasswordAsync security stamp-ı yenilədi — bu sessiya yaşasın,
            // digər cihazlardaki köhnə sessiyalar isə 5 dəqiqə içində düşsün
            await signInManager.RefreshSignInAsync(user);
            return Results.NoContent();
        })
        .RequireAuthorization()
        .RequireAntiforgery();

        group.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization()
        .RequireAntiforgery();

        group.MapGet("/me", async (
            ClaimsPrincipal principal,
            UserManager<ApplicationUser> userManager) =>
        {
            var user = await userManager.GetUserAsync(principal);
            if (user is null)
                return Results.Unauthorized();

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new UserInfo(user.Email!, user.DisplayName, roles, user.CompanyId));
        })
        .RequireAuthorization();
    }
}

public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UserInfo(string Email, string DisplayName, IList<string> Roles, Guid? CompanyId);
