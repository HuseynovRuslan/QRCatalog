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
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<QrCode> QrCodes => Set<QrCode>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Inquiry> Inquiries => Set<Inquiry>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<ScanEvent> ScanEvents => Set<ScanEvent>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

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

        modelBuilder.Entity<Category>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.Property(c => c.Slug).HasMaxLength(220).IsRequired();
            b.Property(c => c.Description).HasMaxLength(2000);
            b.Property(c => c.CodePrefix).HasMaxLength(4);

            // Slug müəssisə daxilində unikaldır — iki müəssisədə eyni slug normaldır
            b.HasIndex(c => new { c.CompanyId, c.Slug }).IsUnique();
            b.HasIndex(c => new { c.CompanyId, c.ParentId, c.SortOrder });

            b.HasOne<Company>()
                .WithMany()
                .HasForeignKey(c => c.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne<Category>()
                .WithMany()
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict); // uşağı olan kateqoriya DB səviyyəsində də silinmir
        });

        modelBuilder.Entity<QrCode>(b =>
        {
            b.Property(q => q.Token).HasMaxLength(24).IsRequired();
            b.Property(q => q.HumanCode).HasMaxLength(12).IsRequired();
            b.Property(q => q.Prefix).HasMaxLength(4).IsRequired();

            // Token QLOBAL unikaldır — URL bütün müəssisələr üzrə ortaqdır.
            // /q/{token} qaynar yolunun yeganə axtarış açarı budur.
            b.HasIndex(q => q.Token).IsUnique();

            // İnsan-oxunan kod isə müəssisə daxilində unikaldır
            b.HasIndex(q => new { q.CompanyId, q.HumanCode }).IsUnique();
            b.HasIndex(q => new { q.CompanyId, q.Prefix, q.Sequence });

            b.HasOne<Company>()
                .WithMany()
                .HasForeignKey(q => q.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.Property(p => p.Slug).HasMaxLength(220).IsRequired();
            b.Property(p => p.Sku).HasMaxLength(60);

            b.HasIndex(p => new { p.CompanyId, p.Slug }).IsUnique();
            b.HasIndex(p => new { p.CompanyId, p.CategoryId, p.Status });

            b.HasOne<Company>()
                .WithMany()
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Məhsulu olan kateqoriya silinməsin — endpoint 409 + DB Restrict
            b.HasOne<Category>()
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(p => p.Translations)
                .WithOne()
                .HasForeignKey(t => t.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(p => p.Specs)
                .WithOne()
                .HasForeignKey(s => s.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(p => p.Images)
                .WithOne()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProductTranslation>(b =>
        {
            b.Property(t => t.Lang).HasMaxLength(2).IsRequired();
            b.Property(t => t.Name).HasMaxLength(300).IsRequired();
            b.HasIndex(t => new { t.ProductId, t.Lang }).IsUnique();
        });

        modelBuilder.Entity<ProductSpec>(b =>
        {
            b.Property(s => s.Label).HasMaxLength(100).IsRequired();
            b.Property(s => s.Value).HasMaxLength(500).IsRequired();
            b.HasIndex(s => new { s.ProductId, s.SortOrder });
        });

        modelBuilder.Entity<ProductImage>(b =>
        {
            b.Property(i => i.StoragePrefix).HasMaxLength(300).IsRequired();
            b.Property(i => i.Widths).HasMaxLength(60).IsRequired();
            b.Property(i => i.AltText).HasMaxLength(300);
            b.HasIndex(i => new { i.ProductId, i.SortOrder });
        });

        modelBuilder.Entity<Inquiry>(b =>
        {
            b.Property(i => i.Name).HasMaxLength(200).IsRequired();
            b.Property(i => i.Phone).HasMaxLength(30).IsRequired();
            b.Property(i => i.Message).HasMaxLength(4000);
            b.Property(i => i.InternalNote).HasMaxLength(4000);

            b.HasIndex(i => new { i.CompanyId, i.Status, i.CreatedAtUtc });

            b.HasOne<Company>().WithMany()
                .HasForeignKey(i => i.CompanyId).OnDelete(DeleteBehavior.Restrict);
            // Məhsul arxivləşsə də sorğu tarixçəsi qalır
            b.HasOne<Product>().WithMany()
                .HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<QrCode>().WithMany()
                .HasForeignKey(i => i.QrCodeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ScanEvent>(b =>
        {
            b.Property(s => s.DeviceKind).HasMaxLength(10).IsRequired();
            b.Property(s => s.Lang).HasMaxLength(10);

            b.HasIndex(s => new { s.CompanyId, s.OccurredAtUtc });
            b.HasIndex(s => s.QrCodeId);

            b.HasOne<QrCode>().WithMany()
                .HasForeignKey(s => s.QrCodeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Site>(b =>
        {
            b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            b.Property(s => s.Address).HasMaxLength(300);
            b.Property(s => s.ContactName).HasMaxLength(200);
            b.Property(s => s.ContactPhone).HasMaxLength(40);
            b.Property(s => s.Note).HasMaxLength(2000);

            b.HasIndex(s => new { s.CompanyId, s.Name });

            b.HasOne<Company>()
                .WithMany()
                .HasForeignKey(s => s.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(s => s.Items)
                .WithOne()
                .HasForeignKey(i => i.SiteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SiteItem>(b =>
        {
            b.HasIndex(i => new { i.SiteId, i.ProductId });

            // Obyektdə işlənən məhsul silinə bilməz — məhsul onsuz da silinmir,
            // arxivləşir; Restrict bu qaydanı bazada da təsbit edir.
            b.HasOne<Product>()
                .WithMany()
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.Property(a => a.UserEmail).HasMaxLength(320).IsRequired();
            b.Property(a => a.EntityType).HasMaxLength(60).IsRequired();
            b.Property(a => a.EntityId).HasMaxLength(60).IsRequired();
            b.Property(a => a.Action).HasMaxLength(10).IsRequired();
            b.Property(a => a.Changes).HasMaxLength(8000);

            b.HasIndex(a => new { a.CompanyId, a.OccurredAtUtc });
        });

        DeclareClientAssignedKeys(modelBuilder);
        ApplyTenantFilters(modelBuilder);
    }

    /// <summary>
    /// Domen entity-lərinin Guid açarını fabrik metodu verir — bazada generasiya OLUNMUR.
    /// Bunu modeldə bildirmək məcburidir: əks halda EF mövcud valideynə əlavə olunan uşağı
    /// (məs. <c>product.Specs.Add(...)</c>) "açarı doludur, deməli mövcud sətirdir" deyə
    /// Added yerinə Modified sayır, INSERT əvəzinə UPDATE göndərir və 0 sətir dəyişdiyi üçün
    /// DbUpdateConcurrencyException atır. İlk CI işəsalmaları bunu spesifikasiya yazılışında tapdı.
    /// Identity cədvəlləri qəsdən kənarda saxlanılır — onların açarını EF özü doldurur.
    /// </summary>
    private static void DeclareClientAssignedKeys(ModelBuilder modelBuilder)
    {
        var domainAssembly = typeof(Product).Assembly;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.Assembly != domainAssembly)
                continue;
            if (entityType.FindPrimaryKey()?.Properties is not [{ ClrType: var clrType } key])
                continue;
            if (clrType != typeof(Guid))
                continue;

            key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
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

            // e => _tenant.CompanyId != null && (Guid?)e.CompanyId == _tenant.CompanyId
            // DİQQƏT: sağ tərəf Guid?-dır, Convert(Guid?→Guid) İŞLƏDİLMİR — EF parametri
            // SQL AND-dən asılı olmayaraq client-də hesablayır və null halında
            // "Nullable object must have a value" ilə partlayırdı (ilk CI run tapdı).
            var tenantAccess = Expression.Property(
                Expression.Field(Expression.Constant(this), "_tenant"),
                nameof(ITenantContext.CompanyId));

            var body = Expression.AndAlso(
                Expression.NotEqual(tenantAccess, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(Expression.Convert(companyId, typeof(Guid?)), tenantAccess));

            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(Expression.Lambda(body, parameter));
        }
    }
}
