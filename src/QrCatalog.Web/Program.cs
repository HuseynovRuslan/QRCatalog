using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using QrCatalog.Infrastructure;
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

    app.UseSerilogRequestLogging();
    app.UseRouting();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();
    app.MapAuthEndpoints();
    app.MapCategoryEndpoints();
    app.MapQrCodeEndpoints();
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
