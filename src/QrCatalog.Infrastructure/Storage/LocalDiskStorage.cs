using Microsoft.Extensions.Configuration;
using QrCatalog.Application.Abstractions;

namespace QrCatalog.Infrastructure.Storage;

/// <summary>
/// Dev/lokal üçün: fayllar diskdə saxlanılır və /uploads altından verilir.
/// Yol qaçışına qarşı hər açar tam yol həllindən sonra kök daxilində yoxlanılır.
/// </summary>
public sealed class LocalDiskStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskStorage(IConfiguration config, string contentRootPath)
    {
        _root = Path.GetFullPath(
            config["Storage:Local:Root"] ?? Path.Combine(contentRootPath, "uploads"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task SaveAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = Resolve(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);
    }

    public Task DeleteByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var fullPath = Resolve(prefix);
        if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string path) => $"/uploads/{path}";

    private string Resolve(string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Yol kökdən kənara çıxır.", nameof(path));
        return fullPath;
    }
}
