using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Common;
using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Identity;

namespace QrCatalog.Infrastructure.Persistence;

public sealed class AppDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    private readonly ITenantContext _tenant;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    public DbSet<Company> Companies => Set<Company>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // Identity cədvəlləri

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.DisplayName).HasMaxLength(200);
            b.HasOne<Company>()
                .WithMany()
                .HasForeignKey(u => u.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.Property(c => c.Slug).HasMaxLength(60).IsRequired();
            b.HasIndex(c => c.Slug).IsUnique();
        });

        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// <see cref="ITenantOwned"/> daşıyan HƏR entity-yə fail-closed filtr qoyur:
    /// _tenant.CompanyId null olduqda sorğu boş qayıdır — səhvən "hamısını göstər" mümkün deyil.
    /// Filtri yalnız super-admin kontekstində IgnoreQueryFilters ilə keçmək olar.
    /// </summary>
    private void ApplyTenantFilters(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var companyId = Expression.Property(parameter, nameof(ITenantOwned.CompanyId));

            // e => _tenant.CompanyId != null && e.CompanyId == _tenant.CompanyId.Value
            var tenantAccess = Expression.Property(
                Expression.Field(Expression.Constant(this), "_tenant"),
                nameof(ITenantContext.CompanyId));

            var body = Expression.AndAlso(
                Expression.NotEqual(tenantAccess, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(companyId, Expression.Convert(tenantAccess, typeof(Guid))));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
