namespace QrCatalog.Application.Abstractions;

/// <summary>
/// Fayl saxlama abstraksiyası. Dev-də lokal disk, produksiyada Cloudflare R2 (S3-uyğun).
/// Yollar həmişə irəli slash-lı nisbi açardır: "products/{id}/{imageId}/w640.webp".
/// </summary>
public interface IFileStorage
{
    Task SaveAsync(string path, Stream content, string contentType, CancellationToken ct = default);

    Task DeleteByPrefixAsync(string prefix, CancellationToken ct = default);

    /// <summary>Brauzerin birbaşa yükləyəcəyi URL.</summary>
    string GetPublicUrl(string path);
}
