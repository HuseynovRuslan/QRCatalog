using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.FileProviders;
using QrCatalog.Application.Abstractions;
using QrCatalog.Infrastructure;
using QrCatalog.Infrastructure.Storage;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Web.Api;
using QrCatalog.Web.Infrastructure;
using QuestPDF.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

QuestPDF.Settings.License = LicenseType.Community;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddRazorPages();

    // Default HtmlEncoder yalnız BasicLatin-i açıq buraxır — Razor @-çıxışında
    // AZ hərfləri (ş, ə, ı...) HTML entity-lərinə çevrilirdi, testlər və HTML həcmi əziyyət
    // çəkirdi. Bütün diapazonları aç; <, >, &, " yenə də encode olunur — XSS qorunması dəyişmir.
    builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(options =>
        options.TextEncoderSettings =
            new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

    // GERİ DÖNÜŞÜ OLMAYAN parametr: bu ünvan QR-ın İÇİNƏ yazılır və etiket çap olunandan
    // sonra dəyişdirilə bilmir. Verilməsə kod sorğunun host-una keçir — proxy arxasında bu
    // asanlıqla http://server-ip:8080 kimi bir şey ola bilər və minlərlə etiket ölü çıxar.
    // Ona görə produksiyada tətbiq bu açar olmadan QALXMIR (bax ops/README.md, S2-01).
    if (!builder.Environment.IsDevelopment() &&
        string.IsNullOrWhiteSpace(builder.Configuration["Qr:PublicBaseUrl"]))
    {
        throw new InvalidOperationException(
            "Qr__PublicBaseUrl verilməyib. Bu ünvan QR kodların içinə yazılır və çapdan sonra " +
            "dəyişdirilə bilmir, ona görə produksiyada təsadüfi host-a bel bağlamaq qadağandır. " +
            "Nümunə: Qr__PublicBaseUrl=https://qr.sirket.az (sonda / olmadan).");
    }

    // Data Protection açarları: auth cookie-ləri və antiforgery tokenləri bunlarla imzalanır.
    // Default halda açarlar konteynerin içindədir — hər `up --build` bütün adminləri
    // sistemdən çıxarır və açıq formaları etibarsız edir. Volume-a yazılanda deploy sakit keçir.
    var keysPath = builder.Configuration["DataProtection:KeysPath"];
    if (!string.IsNullOrWhiteSpace(keysPath))
    {
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("QrCatalog");
    }

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<QrCatalog.Application.Abstractions.ICurrentUser,
        QrCatalog.Web.Infrastructure.CurrentUserAccessor>();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddAppAuthorization();
    builder.Services.AddOpenApi();

    builder.Services.AddAntiforgery(options => options.HeaderName = "X-XSRF-TOKEN");

    // Public səhifələr: 5 dəq keş + "public" teqi. İstənilən public entity dəyişəndə
    // PublicCacheInvalidationInterceptor teqi boşaldır — köhnə səhifə qalmır.
    builder.Services.AddOutputCache(options =>
        options.AddPolicy("public", policy => policy
            .Expire(TimeSpan.FromMinutes(5))
            .Tag(QrCatalog.Infrastructure.Persistence.PublicCacheInvalidationInterceptor.Tag)));

    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
        options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
            }));
        // Public QR həlli — token gəzmə cəhdini boğur, normal skan axınına toxunmur
        options.AddPolicy("qr-resolve", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
            }));
        // Sorğu forması — spam-a qarşı honeypot-un yanında ikinci sədd
        options.AddPolicy("inquiry", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
            }));
    });

    // Produksiyada tətbiq reverse-proxy (Caddy) arxasındadır: TLS orada bitir və sorğular
    // konteynerə düz HTTP kimi çatır. Proxy başlıqları oxunmasa üç şey SƏSSİZCƏ sınır:
    //  1. rate limit hər ziyarətçini deyil, proxy-nin TƏK IP-sini sayır — "5 müraciət/dəq"
    //     bütün sayta ümumi olur və altıncı müştəri rədd edilir;
    //  2. Request.IsHttps false qalır → auth cookie Secure bayrağı almır, UseHsts başlığı
    //     heç vaxt göndərilmir (HstsMiddleware yalnız HTTPS sorğularda işləyir);
    //  3. request-dən qurulan URL-lər http:// olur — QR-ın içinə düşən ünvan da (!).
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

        // Konteyner şəbəkəsində proxy-nin IP-si loopback deyil və qabaqcadan bilinmir,
        // ona görə default siyahılar boşaldılır. ŞƏRT: tətbiq portu yalnız proxy-yə açıq
        // olmalıdır (docker-compose.yml onu 127.0.0.1-ə bağlayır), birbaşa internetə YOX —
        // əks halda kənar adam X-Forwarded-For uydurub rate limit-i və IP-ni aldadar.
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // Health check-lər (postgres daxil) AddInfrastructure-da qeydiyyata alınır

    var app = builder.Build();

    // Ən başda: bundan sonra gələn hər şey (HSTS, cookie, rate limit, URL qurma)
    // düzgün sxem və ziyarətçi IP-si görür
    app.UseForwardedHeaders();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    // TLS produksiyada Caddy-də bitir — konteynerlər öz aralarında düz HTTP danışır,
    // ona görə UseHttpsRedirection yoxdur.

    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseResponseCompression();
    app.UseSerilogRequestLogging();

    // Lokal storage-da yüklənən şəkillər /uploads altından verilir.
    // MapStaticAssets bunun üçün İŞLƏMİR — o, build-vaxtı manifestlə işləyir,
    // runtime-da yaranan fayllardan xəbərsizdir.
    if (app.Services.GetRequiredService<IFileStorage>() is LocalDiskStorage localStorage)
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(localStorage.Root),
            RequestPath = "/uploads",
        });
    }

    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.UseOutputCache();
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();
    app.MapAuthEndpoints();
    app.MapCategoryEndpoints();
    app.MapProductEndpoints();
    app.MapQrCodeEndpoints();
    app.MapSettingsEndpoints();
    app.MapInquiryEndpoints();
    app.MapSiteEndpoints();
    app.MapStatsEndpoints();
    app.MapAuditEndpoints();
    app.MapHealthChecks("/health");

    // Admin SPA — dərin linklər (/admin/mehsullar və s.) index.html-ə düşür, marşrutu React həll edir
    app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(); // /scalar — API sənədi
    }

    await app.Services.MigrateDatabaseAsync();
    await app.Services.SeedBootstrapAdminAsync();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Host gözlənilmədən dayandı");

    // Sıfır çıxış kodu Docker-ə "uğurla bitdi" deyir: `restart: unless-stopped` altında
    // konteyner səssizcə dövrə vurur və `docker compose ps` heç bir problem göstərmir.
    // Konfiqurasiya xətası GÖRÜNƏN olmalıdır.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>WebApplicationFactory-nin inteqrasiya testlərində görə bilməsi üçün.</summary>
public partial class Program { }
