namespace QrCatalog.Web.Pages;

/// <summary>
/// Public səhifələrin görünüş modelləri. QƏSDƏN ayrı tiplər: daxili sahələr (qiymət, qeydlər,
/// status və s.) bu strukturlarda MÖVCUD DEYİL — səhvən sızdırmaq mümkün olmasın.
/// </summary>
public sealed record PublicImageVm(string? Alt, IReadOnlyList<PublicImageVariantVm> Variants)
{
    public string Srcset => string.Join(", ", Variants.Select(v => $"{v.Url} {v.Width}w"));
    public string? MainUrl => Variants.Count > 0
        ? (Variants.FirstOrDefault(v => v.Width >= 640) ?? Variants[^1]).Url
        : null;
    public string? ThumbUrl => Variants.Count > 0 ? Variants[0].Url : null;
}

public sealed record PublicImageVariantVm(int Width, string Url);

public sealed record PublicSpecVm(string Label, string Value);

public sealed record PublicProductVm(
    string Name,
    string? Description,
    string Slug,
    string CategoryName,
    string CategorySlug,
    IReadOnlyList<PublicSpecVm> Specs,
    IReadOnlyList<PublicImageVm> Images,
    string? HumanCode,
    string? Phone,
    string? WhatsappNumber,
    string? QrToken = null)
{
    public string? WhatsappUrl => WhatsappNumber is null
        ? null
        : $"https://wa.me/{WhatsappNumber}?text={Uri.EscapeDataString($"Salam! \"{Name}\"{(HumanCode is null ? "" : $" ({HumanCode})")} haqqında məlumat almaq istəyirəm.")}";
}

public sealed record PublicProductCardVm(
    string Name, string Slug, string CategoryName, string? ThumbUrl, string? ThumbSrcset);

public sealed record PublicCategoryVm(string Name, string Slug, string? Description, int ProductCount);

public sealed record BreadcrumbVm(string Name, string? Url);
