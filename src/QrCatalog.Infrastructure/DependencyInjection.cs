using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Infrastructure.Persistence;
using QrCatalog.Infrastructure.Tenancy;

namespace QrCatalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection tapılmadı — appsettings və ya env dəyişənini yoxla.");

        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure()));

        services.AddIdentityAuth();

        return services;
    }

    private static void AddIdentityAuth(this IServiceCollection services)
    {
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "qrcatalog.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // M8: Caddy arxasında ForwardedHeaders qoşulanda Always olacaq
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);

            // SPA/API cookie sxemində redirect mənasızdır — status kodu qaytar
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    /// <summary>
    /// Migration-ları startup-da tətbiq edir. Baza hələ qalxmayıbsa bir neçə dəfə yenidən cəhd edir;
    /// alınmasa tətbiq YIXILMIR — sayt işləyir, /health qırmızı qalır və problem orada görünür.
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, int attempts = 5)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Migrations");

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Migration-lar tətbiq olundu (cəhd {Attempt}).", attempt);
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                logger.LogWarning(ex,
                    "Bazaya qoşulmaq alınmadı (cəhd {Attempt}/{Attempts}), 3 saniyə sonra təkrar.",
                    attempt, attempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Migration-lar tətbiq oluna bilmədi — tətbiq bazasız davam edir, /health yoxla.");
            }
        }
    }
}
