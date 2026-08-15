using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QrCatalog.Application.Abstractions;
using QrCatalog.Domain.Entities;

namespace QrCatalog.Infrastructure.Persistence;

/// <summary>
/// Tenant-scoped biznes entity-lərinin hər dəyişikliyini AuditEntry kimi eyni tranzaksiyada yazır.
/// Mərkəzi nöqtədir — endpoint-lər audit-dən xəbərsizdir, unutmaq mümkün deyil.
/// ScanEvent (həcm) və AuditEntry özü (rekursiya) qəsdən siyahıda deyil.
/// Tenant məlum deyilsə (seed, migration) audit yazılmır — sistem əməliyyatlarıdır.
/// </summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private const int MaxValueLength = 500;

    private static readonly Type[] AuditedTypes =
    [
        typeof(Product), typeof(ProductTranslation), typeof(ProductSpec), typeof(ProductImage),
        typeof(Category), typeof(QrCode), typeof(Company), typeof(Inquiry),
    ];

    private readonly ICurrentUser? _currentUser;
    private readonly ITenantContext? _tenant;

    public AuditInterceptor(ICurrentUser? currentUser, ITenantContext? tenant)
    {
        _currentUser = currentUser;
        _tenant = tenant;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        CollectAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void CollectAuditEntries(DbContext? context)
    {
        if (context is null || _tenant?.CompanyId is not { } companyId)
            return;

        var email = _currentUser?.Email ?? "(sistem)";

        // Materiallaşdır — kolleksiyaya AuditEntry əlavə edərkən iterator pozulmasın
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                        AuditedTypes.Contains(e.Entity.GetType()))
            .ToList();

        foreach (var entry in entries)
        {
            var action = entry.State switch
            {
                EntityState.Added => "added",
                EntityState.Deleted => "deleted",
                _ => "modified",
            };

            string? changes = null;
            if (entry.State == EntityState.Modified)
            {
                var diff = ChangedProperties(entry);
                if (diff.Count == 0)
                    continue; // real dəyişiklik yoxdur
                changes = JsonSerializer.Serialize(diff);
            }

            var id = entry.Properties
                .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString() ?? "?";

            context.Set<AuditEntry>().Add(AuditEntry.Create(
                companyId, email, entry.Entity.GetType().Name, id, action, changes));
        }
    }

    private static Dictionary<string, string?[]> ChangedProperties(EntityEntry entry)
    {
        var diff = new Dictionary<string, string?[]>();
        foreach (var property in entry.Properties)
        {
            if (!property.IsModified || property.Metadata.IsPrimaryKey())
                continue;

            var oldValue = Truncate(property.OriginalValue?.ToString());
            var newValue = Truncate(property.CurrentValue?.ToString());
            if (oldValue == newValue)
                continue;

            diff[property.Metadata.Name] = [oldValue, newValue];
        }
        return diff;
    }

    private static string? Truncate(string? value) =>
        value is { Length: > MaxValueLength } ? value[..MaxValueLength] + "…" : value;
}
