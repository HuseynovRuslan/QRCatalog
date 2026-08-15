using System.Security.Claims;
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

            var result = await signInManager.PasswordSignInAsync(
                user, request.Password, isPersistent: true, lockoutOnFailure: true);

            if (result.IsLockedOut)
                return Results.Problem(statusCode: StatusCodes.Status423Locked,
                    title: "Çox sayda uğursuz cəhd — hesab 5 dəqiqəlik kilidləndi.");
            if (!result.Succeeded)
                return Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "E-poçt və ya parol yanlışdır.");

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new UserInfo(user.Email!, user.DisplayName, roles, user.CompanyId));
        })
        .RequireRateLimiting("login")
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

public sealed record LoginRequest(string Email, string Password);

public sealed record UserInfo(string Email, string DisplayName, IList<string> Roles, Guid? CompanyId);
