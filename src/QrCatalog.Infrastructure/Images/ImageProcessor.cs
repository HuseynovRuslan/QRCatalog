using ImageMagick;

namespace QrCatalog.Infrastructure.Images;

public sealed record ProcessedImage(int Width, byte[] WebpBytes);

public sealed record ProcessResult(IReadOnlyList<ProcessedImage> Variants);

/// <summary>
/// Yüklənən şəkli yoxlayır və ölçü variantlarına çevirir. Qaydalar:
/// magic-byte yoxlanılır (uzantıya inanılmır), EXIF orientasiyası tətbiq olunub
/// metadata (GPS daxil) atılır, böyüdülmə yoxdur. Format: WebP.
/// (Texnoloji sənəddəki ImageSharp v4 build-də ödənişli lisenziya açarı tələb etdiyi üçün
/// Apache 2.0 lisenziyalı Magick.NET işlədilir; "AVIF" də bu səbəbdən WebP-yə endirildi.)
/// </summary>
public sealed class ImageProcessor
{
    public static readonly int[] TargetWidths = [320, 640, 1280, 1920];
    public const int MaxUploadBytes = 15 * 1024 * 1024;

    public bool LooksLikeSupportedImage(ReadOnlySpan<byte> head)
    {
        if (head.Length < 12) return false;

        // JPEG: FF D8 FF
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return true;
        // PNG: 89 50 4E 47
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return true;
        // WebP: "RIFF"...."WEBP"
        if (head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8)) return true;

        return false;
    }

    public async Task<ProcessResult> ProcessAsync(Stream input, CancellationToken ct = default)
    {
        // Konstruktor həm də ikinci validasiyadır — korlanmış fayl burada MagickException atır
        using var image = new MagickImage(input);
        image.AutoOrient();     // EXIF orientasiyasını piksellərə tətbiq et
        image.Strip();          // EXIF/GPS/profil metadata-sını at

        var variants = new List<ProcessedImage>();

        foreach (var width in TargetWidths)
        {
            ct.ThrowIfCancellationRequested();

            var targetWidth = Math.Min((uint)width, image.Width);
            using var resized = (MagickImage)image.Clone();
            if (targetWidth < image.Width)
                resized.Resize(targetWidth, 0); // hündürlük proporsional

            resized.Quality = 80;
            resized.Format = MagickFormat.WebP;

            using var ms = new MemoryStream();
            await resized.WriteAsync(ms, ct);
            variants.Add(new ProcessedImage((int)targetWidth, ms.ToArray()));

            if (targetWidth == image.Width)
                break; // orijinal endən böyük variantların mənası yoxdur — böyütmə yoxdur
        }

        return new ProcessResult(variants);
    }
}
