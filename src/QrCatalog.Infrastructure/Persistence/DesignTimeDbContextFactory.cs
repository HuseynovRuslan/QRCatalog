using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using QrCatalog.Infrastructure.Tenancy;

namespace QrCatalog.Infrastructure.Persistence;

/// <summary>
/// Yalnız `dotnet ef` üçün — migration GENERASİYASINA bağlantı lazım deyil, sxem kifayətdir.
/// Amma `migrations remove` tətbiq olunub-olunmadığını yoxlamaq üçün bazaya girir və sabit
/// sətir səhv porta baxdığından sınırdı. Ona görə mühit dəyişəni üstün gəlir:
/// `ConnectionStrings__DefaultConnection=... dotnet ef migrations remove ...`
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connection =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Database=qrcatalog_design;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection)
            .Options;

        return new AppDbContext(options, NullTenantContext.Instance);
    }
}
