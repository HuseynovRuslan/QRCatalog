using QrCatalog.Domain.Entities;
using QrCatalog.Infrastructure.Qr;

namespace QrCatalog.UnitTests;

public class QrCodeTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();

    [Fact]
    public void Create_FormatsHumanCode()
    {
        var qr = QrCode.Create(CompanyId, "tok123", "SZ", 142, QrTargetType.Category, TargetId);

        Assert.Equal("SZ-0142", qr.HumanCode);
        Assert.Equal(QrCodeStatus.Active, qr.Status);
    }

    [Fact]
    public void Create_ArchiveWithTargetId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            QrCode.Create(CompanyId, "tok", "QR", 1, QrTargetType.Archive, TargetId));
    }

    [Fact]
    public void Create_CategoryWithoutTargetId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            QrCode.Create(CompanyId, "tok", "QR", 1, QrTargetType.Category, null));
    }

    [Fact]
    public void Retarget_ToArchive_ClearsTarget()
    {
        var qr = QrCode.Create(CompanyId, "tok", "SZ", 1, QrTargetType.Category, TargetId);

        qr.Retarget(QrTargetType.Archive, null);

        Assert.Equal(QrTargetType.Archive, qr.TargetType);
        Assert.Null(qr.TargetId);
    }

    [Fact]
    public void RetireAndReactivate_TogglesStatus()
    {
        var qr = QrCode.Create(CompanyId, "tok", "SZ", 1, QrTargetType.Category, TargetId);

        qr.Retire();
        Assert.Equal(QrCodeStatus.Retired, qr.Status);

        qr.Reactivate();
        Assert.Equal(QrCodeStatus.Active, qr.Status);
    }
}

public class TokenGeneratorTests
{
    [Fact]
    public void Next_HasExpectedLengthAndCharset()
    {
        for (var i = 0; i < 100; i++)
        {
            var token = TokenGenerator.Next();
            Assert.Equal(TokenGenerator.Length, token.Length);
            Assert.All(token, c => Assert.True(char.IsAsciiLetterOrDigit(c)));
        }
    }

    [Fact]
    public void Next_DoesNotRepeat()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => TokenGenerator.Next()).ToHashSet();
        Assert.Equal(1000, tokens.Count);
    }
}

public class QrImageServiceTests
{
    [Fact]
    public void RenderSvg_ProducesSvgMarkup()
    {
        var svg = new QrImageService().RenderSvg("https://example.az/q/abc123");
        Assert.Contains("<svg", svg);
    }

    [Fact]
    public void RenderPng_ProducesPngBytes()
    {
        var png = new QrImageService().RenderPng("https://example.az/q/abc123");
        // PNG imzası: 89 50 4E 47
        Assert.True(png.Length > 100);
        Assert.Equal(0x89, png[0]);
        Assert.Equal(0x50, png[1]);
    }
}
