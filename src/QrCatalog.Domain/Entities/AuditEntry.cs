using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

/// <summary>
/// Fəaliyyət jurnalı sətri: kim, nə vaxt, nəyi, hansı dəyərdən hansına.
/// AuditInterceptor tərəfindən avtomatik yazılır — endpoint-lər bundan xəbərsizdir.
/// </summary>
public sealed class AuditEntry : ITenantOwned
{
    private AuditEntry() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string UserEmail { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;

    /// <summary>added · modified · deleted</summary>
    public string Action { get; private set; } = null!;

    /// <summary>Dəyişən sahələr JSON: {"Name": ["köhnə", "yeni"]}. Added/deleted-də null.</summary>
    public string? Changes { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static AuditEntry Create(
        Guid companyId, string userEmail, string entityType, string entityId,
        string action, string? changes)
    {
        return new AuditEntry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            UserEmail = userEmail,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Changes = changes,
            OccurredAtUtc = DateTime.UtcNow,
        };
    }
}
