using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QrCatalog.Domain.Entities;

namespace QrCatalog.Infrastructure.Persistence;

/// <summary>
/// Public səhifələrə təsir edən hər hansı entity dəyişəndə "public" output-cache teqini boşaldır.
/// Tək mərkəzi nöqtə — endpoint-lər keşdən xəbərsizdir, unutmaq mümkün deyil.
/// Save-dən ƏVVƏL boşaldılır: uğursuz save-də artıq eviction zərərsizdir, əksi (yaddan çıxmış
/// eviction) isə köhnə səhifə deməkdir.
/// </summary>
public sealed class PublicCacheInvalidationInterceptor : SaveChangesInterceptor
{
    public const string Tag = "public";

    private static readonly Type[] PublicTypes =
    [
        typeof(Product), typeof(ProductTranslation), typeof(ProductSpec), typeof(ProductImage),
        typeof(Category), typeof(QrCode), typeof(Company),
    ];

    private readonly IOutputCacheStore? _store;

    public PublicCacheInvalidationInterceptor(IOutputCacheStore? store) => _store = store;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (_store is not null && eventData.Context is { } context && HasPublicChanges(context))
            await _store.EvictByTagAsync(Tag, cancellationToken);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (_store is not null && eventData.Context is { } context && HasPublicChanges(context))
            _store.EvictByTagAsync(Tag, CancellationToken.None).AsTask().GetAwaiter().GetResult();

        return base.SavingChanges(eventData, result);
    }

    private static bool HasPublicChanges(DbContext context) =>
        context.ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
            PublicTypes.Contains(e.Entity.GetType()));
}
