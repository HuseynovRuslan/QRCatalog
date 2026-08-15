using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        // Boş sətir də rədd edilir: appsettings-də açar "" ilə mövcuddursa, ?? null-guard
        // keçərdi və tətbiq işlək görünərdi — konfiqurasiya xətası fail-fast olmalıdır.
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection boşdur və ya yoxdur — appsettings və ya env dəyişənini yoxla.");

        services.AddScoped<AmbientTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<AmbientTenantContext>());

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure());

            // Output cache qeydiyyatda yoxdursa (məs. dizayn-vaxtı) interceptor passiv qalır
            options.AddInterceptors(new PublicCacheInvalidationInterceptor(
                sp.GetService<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore>()));
        });

        // Health check bağlantı sətri ilə birlikdə burada qeydiyyata alınır ki,
        // "sətir var amma check yoxdur" uyğunsuzluğu mümkün olmasın
        services.AddHealthChecks().AddNpgSql(connectionString, name: "postgres");

        services.AddIdentityAuth();

        services.AddSingleton<Qr.QrImageService>();
        services.AddSingleton<Qr.LabelSheetService>();
        services.AddSingleton<Images.ImageProcessor>();

        // Storage:Provider = "S3" (R2) | "Local" (default — dev)
        services.AddSingleton<IFileStorage>(sp =>
            configuration["Storage:Provider"]?.Equals("S3", StringComparison.OrdinalIgnoreCase) == true
                ? new Storage.S3Storage(configuration)
                : new Storage.LocalDiskStorage(configuration,
                    sp.GetRequiredService<IHostEnvironment>().ContentRootPath));

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
    /// alınmasa tətbiq YIXILMIR — sayt qalxır, /health qırmızı qalır və problem orada görünür.
    /// QƏSDƏN retry-siz ayrıca DbContext işlədilir: DI-dakı EnableRetryOnFailure ilə MigrateAsync
    /// iç-içə retry edərdi (hər xarici cəhdin daxilində ~1 dəqiqəlik seriya) və DB sönülü olanda
    /// startup dəqiqələrlə bloklanardı. İndi ən pis hal ≈ 5×(qoşulma taymautu+3s).
    /// </summary>
    public static async Task MigrateDatabaseAsync(this IServiceProvider services, int attempts = 5)
    {
        using var scope = services.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Migrations");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(config.GetConnectionString("DefaultConnection"))
            .Options;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using var db = new AppDbContext(options, NullTenantContext.Instance);
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
