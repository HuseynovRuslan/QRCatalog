using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace QrCatalog.Infrastructure.Qr;

public sealed record LabelItem(string HumanCode, string Url, string TargetName);

/// <summary>
/// A4 etiket vərəqi — 3 sütun × 7 sətir = səhifədə 21 etiket (etiket ~63.5×38 mm,
/// Avery L7160 tipli vərəqlərə uyğun). Ölçülər millimetrlə verilir — QR çox kiçik
/// çap olunsa telefon oxumur, ona görə burada dəqiqlik kritikdir.
/// </summary>
public sealed class LabelSheetService
{
    private const float LabelWidthMm = 63.5f;
    private const float LabelHeightMm = 38.1f;
    private const int Columns = 3;
    private const int RowsPerPage = 7;

    private readonly QrImageService _qrImages;

    public LabelSheetService(QrImageService qrImages) => _qrImages = qrImages;

    public byte[] RenderSheet(IReadOnlyList<LabelItem> items, string scanHint = "Məlumat üçün skan edin")
    {
        return Document.Create(container =>
        {
            foreach (var pageItems in items.Chunk(Columns * RowsPerPage))
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal((210 - Columns * LabelWidthMm) / 2, Unit.Millimetre);
                    page.MarginVertical((297 - RowsPerPage * LabelHeightMm) / 2, Unit.Millimetre);

                    page.Content().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            for (var i = 0; i < Columns; i++)
                                columns.ConstantColumn(LabelWidthMm, Unit.Millimetre);
                        });

                        foreach (var item in pageItems)
                        {
                            table.Cell()
                                .Height(LabelHeightMm, Unit.Millimetre)
                                .Padding(2, Unit.Millimetre)
                                .Row(row =>
                                {
                                    // QR — etiketin sol tərəfində, hündürlüyü tam tutur (~30mm)
                                    row.ConstantItem(30, Unit.Millimetre)
                                        .AlignMiddle()
                                        .Image(_qrImages.RenderPng(item.Url))
                                        .FitArea();

                                    row.RelativeItem()
                                        .PaddingLeft(2, Unit.Millimetre)
                                        .AlignMiddle()
                                        .Column(col =>
                                        {
                                            // FontFamily QƏSDƏN verilmir: Consolas Linux serverdə
                                            // yoxdur və QuestPDF exception atırdı (ilk CI run tapdı)
                                            col.Item().Text(item.HumanCode)
                                                .Bold().FontSize(12);
                                            col.Item().PaddingTop(1, Unit.Millimetre)
                                                .Text(item.TargetName).FontSize(8);
                                            col.Item().PaddingTop(2, Unit.Millimetre)
                                                .Text(scanHint).FontSize(7).Light();
                                        });
                                });
                        }
                    });
                });
            }
        }).GeneratePdf();
    }
}
