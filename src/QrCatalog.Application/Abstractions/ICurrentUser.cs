namespace QrCatalog.Application.Abstractions;

/// <summary>Cari sorğunun istifadəçisi — audit jurnalı üçün. Anonim sorğuda hər ikisi null.</summary>
public interface ICurrentUser
{
    string? Email { get; }
}
