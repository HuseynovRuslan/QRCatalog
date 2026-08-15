using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QrCatalog.Infrastructure.Tenancy;

namespace QrCatalog.Infrastructure.Persistence;

/// <summary>
/// Yalnız `dotnet ef` üçün — real bağlantı açılmır, migration generasiyasına sxem lazımdır.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=qrcatalog_design;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options, NullTenantContext.Instance);
    }
}
