using QrCatalog.Infrastructure;
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
    builder.Services.AddOpenApi();

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
    app.UseAuthorization();

    app.MapStaticAssets();
    app.MapRazorPages().WithStaticAssets();
    app.MapHealthChecks("/health");

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(); // /scalar — API sənədi
    }

    await app.Services.MigrateDatabaseAsync();

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
