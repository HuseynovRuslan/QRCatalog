using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using QrCatalog.Infrastructure;
using QrCatalog.Infrastructure.Identity;
using QrCatalog.Web.Api;
using QrCatalog.Web.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

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
    });

    var healthChecks = builder.Services.AddHealthChecks();
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrEmpty(connectionString))
        healthChecks.AddNpgSql(connectionString, name: "postgres");

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
