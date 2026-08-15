using System.Threading.RateLimiting;
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

    // Health check-lər (postgres daxil) AddInfrastructure-da qeydiyyata alınır

    var app = builder.Build();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
    }

    // TLS produksiyada Caddy-də bitir — konteynerlər öz aralarında düz HTTP danışır,
    // ona görə UseHttpsRedirection yoxdur.

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
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>WebApplicationFactory-nin inteqrasiya testlərində görə bilməsi üçün.</summary>
public partial class Program { }
