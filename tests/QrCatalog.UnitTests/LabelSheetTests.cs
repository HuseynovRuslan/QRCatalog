using System.Text;
using QrCatalog.Infrastructure.Qr;
using QuestPDF.Infrastructure;

namespace QrCatalog.UnitTests;

/// <summary>
/// Etiket vərəqi fiziki məhsuldur — səhv çap olunmuş QR-ı geri qaytarmaq mümkün deyil.
/// Bu testlər PDF generasiyasını Docker olmadan, hər platformada yoxlayır: qüsur ilk dəfə
/// yalnız Linux CI-da (şrift yoxdur) üzə çıxmışdı, ona görə mühitdən asılılıq qadağandır.
/// </summary>
public class LabelSheetTests
{
    static LabelSheetTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = true;
    }

    private static readonly LabelSheetService Sheets = new(new QrImageService());

    [Fact]
    public void RenderSheet_AzerbaijaniText_ProducesPdf()
    {
        // Adda ə, ş, ğ, ı, ö, ü — Azərbaycan əlifbasının şriftdə çatmayan hərfləri
        var items = new List<LabelItem>
        {
            new("SZ-0001", "https://katalog.example.az/q/aB3xY9kLm2Q", "Bahama şezlonq, ağ"),
            new("SK-0142", "https://katalog.example.az/q/Zq7Wn4pRt8V", "Bağ skamyası — İpək"),
        };

        var pdf = Sheets.RenderSheet(items);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf[..4]));
        Assert.True(pdf.Length > 2000, $"PDF şübhəli kiçikdir: {pdf.Length} bayt");
    }

    [Fact]
    public void RenderSheet_MoreThanOnePage_StillRenders()
    {
        // 21 etiket bir A4 səhifəyə sığır — 25 ədəd ikinci səhifəni məcbur edir
        var items = Enumerable.Range(1, 25)
            .Select(i => new LabelItem($"SZ-{i:D4}", $"https://katalog.example.az/q/token{i:D5}xx",
                "Şezlonq nümunəsi"))
            .ToList();

        var pdf = Sheets.RenderSheet(items);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf[..4]));
    }
}

