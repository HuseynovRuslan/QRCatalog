using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

public enum InquiryStatus
{
    New = 0,
    InProgress = 1,
    Answered = 2,
    Closed = 3,
}

/// <summary>
/// Public saytdan gələn sorğu. Mənbəyi qeyd olunur: hansı məhsuldan, hansı QR-dan —
/// müdürün "QR işə yarayır?" sualının cavabı bu bağlantılardır.
/// </summary>
public sealed class Inquiry : ITenantOwned
{
    private Inquiry() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }

    /// <summary>Sorğu hansı məhsulun səhifəsindən gəldi (ümumi müraciətdə null).</summary>
    public Guid? ProductId { get; private set; }

    /// <summary>Səhifə QR skanından açılmışdısa — hansı kod.</summary>
    public Guid? QrCodeId { get; private set; }

    public string Name { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public string? Message { get; private set; }

    public InquiryStatus Status { get; private set; }

    /// <summary>Daxili qeyd — müştəri görmür.</summary>
    public string? InternalNote { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Inquiry Create(
        Guid companyId, Guid? productId, Guid? qrCodeId,
        string name, string phone, string? message)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Ad boş ola bilməz.", nameof(name));

        var digits = new string((phone ?? "").Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        if (digits.Length < 9)
            throw new ArgumentException("Telefon nömrəsi düzgün deyil.", nameof(phone));

        var now = DateTime.UtcNow;
        return new Inquiry
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ProductId = productId,
            QrCodeId = qrCodeId,
            Name = name.Trim(),
            Phone = digits,
            Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim(),
            Status = InquiryStatus.New,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void SetStatus(InquiryStatus status)
    {
        Status = status;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetNote(string? note)
    {
        InternalNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
