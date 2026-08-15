using ImageMagick;
using QrCatalog.Infrastructure.Images;

namespace QrCatalog.UnitTests;

public class ImageProcessorTests
{
    private static MemoryStream MakePng(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.SeaGreen, width, height);
        image.Format = MagickFormat.Png;
        var ms = new MemoryStream();
        image.Write(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void LooksLikeSupportedImage_AcceptsRealFormats()
    {
        var processor = new ImageProcessor();

        using var png = MakePng(10, 10);
        var head = new byte[12];
        _ = png.Read(head, 0, 12);
        Assert.True(processor.LooksLikeSupportedImage(head));

        Assert.True(processor.LooksLikeSupportedImage(
            new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 })); // JPEG
    }

    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0, 0, 0, 0, 0, 0, 0, 0 })] // %PDF
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 })] // MZ (exe)
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0, 0, 0, 0, 0, 0, 0, 0 })] // <svg (XSS riski)
    public void LooksLikeSupportedImage_RejectsNonImages(byte[] head)
    {
        Assert.False(new ImageProcessor().LooksLikeSupportedImage(head));
    }

    [Fact]
    public async Task Process_LargeImage_ProducesAllWidthsAsWebp()
    {
        using var input = MakePng(2400, 1200);
        var result = await new ImageProcessor().ProcessAsync(input);

        Assert.Equal([320, 640, 1280, 1920], result.Variants.Select(v => v.Width));

        foreach (var variant in result.Variants)
        {
            // WebP imzası: RIFF....WEBP
            Assert.Equal("RIFF"u8.ToArray(), variant.WebpBytes[..4]);
            Assert.Equal("WEBP"u8.ToArray(), variant.WebpBytes[8..12]);

            using var decoded = new MagickImage(variant.WebpBytes);
            Assert.Equal((uint)variant.Width, decoded.Width);
        }
    }

    [Fact]
    public async Task Process_SmallImage_DoesNotUpscale()
    {
        using var input = MakePng(500, 400);
        var result = await new ImageProcessor().ProcessAsync(input);

        // 320 və orijinal 500 — 640+ variantları yaradılmır
        Assert.Equal([320, 500], result.Variants.Select(v => v.Width));
    }

    [Fact]
    public async Task Process_CorruptFile_Throws()
    {
        using var garbage = new MemoryStream("bu şəkil deyil"u8.ToArray());
        await Assert.ThrowsAnyAsync<MagickException>(
            () => new ImageProcessor().ProcessAsync(garbage));
    }
}
