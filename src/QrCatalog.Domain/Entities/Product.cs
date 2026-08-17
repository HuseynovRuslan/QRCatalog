using QrCatalog.Domain.Common;

namespace QrCatalog.Domain.Entities;

public enum ProductStatus
{
    /// <summary>Hazırlanır — public saytda görünmür.</summary>
    Draft = 0,

    /// <summary>Dərc olunub — public saytda görünür.</summary>
    Published = 1,

    /// <summary>İstehsaldan çıxıb. Məhsul HEÇ VAXT silinmir — satılmış nüsxələrin
    /// QR-ları yaşayır; arxiv səhifəsi "istehsal olunmur" deyir.</summary>
    Archived = 2,
}

/// <summary>
/// Məhsul modeli — QR-ın əsas hədəfi. Ç-04 qərarı: hər rəng/ölçü AYRI məhsuldur,
/// variant qatı yoxdur. Ad/təsvir kimi mətnlər dil üzrə <see cref="ProductTranslation"/>-dadır.
/// </summary>
public sealed class Product : ITenantOwned
{
    private Product() { } // EF Core

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CategoryId { get; private set; }

    /// <summary>Public URL üçün, müəssisə daxilində unikal. AZ adından generasiya olunur.</summary>
    public string Slug { get; private set; } = null!;

    /// <summary>Daxili artikul — istəyə görə.</summary>
    public string? Sku { get; private set; }

    public ProductStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public List<ProductTranslation> Translations { get; private set; } = [];
    public List<ProductSpec> Specs { get; private set; } = [];
    public List<ProductImage> Images { get; private set; } = [];

    public static Product Create(Guid companyId, Guid categoryId, string slug, string? sku)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId boş ola bilməz.", nameof(companyId));
        if (categoryId == Guid.Empty)
            throw new ArgumentException("Kateqoriya seçilməlidir.", nameof(categoryId));
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug boş ola bilməz.", nameof(slug));

        var now = DateTime.UtcNow;
        return new Product
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            CategoryId = categoryId,
            Slug = slug,
            Sku = NormalizeSku(sku),
            Status = ProductStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    public void Update(Guid categoryId, string? sku)
    {
        if (categoryId == Guid.Empty)
            throw new ArgumentException("Kateqoriya seçilməlidir.", nameof(categoryId));
        CategoryId = categoryId;
        Sku = NormalizeSku(sku);
        Touch();
    }

    public void Publish()
    {
        Status = ProductStatus.Published;
        Touch();
    }

    /// <summary>Dərcdən geri qaralamaya — arxivdən fərqlidir, "hazır deyil" deməkdir.</summary>
    public void Unpublish()
    {
        Status = ProductStatus.Draft;
        Touch();
    }

    public void Archive()
    {
        Status = ProductStatus.Archived;
        Touch();
    }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string? NormalizeSku(string? sku) =>
        string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
}

/// <summary>Məhsul mətnlərinin dil üzrə saxlanması. F1-də yalnız "az"; RU/EN sonra əlavə olunur.</summary>
public sealed class ProductTranslation
{
    private ProductTranslation() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>ISO 639-1: "az", "ru", "en".</summary>
    public string Lang { get; private set; } = null!;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public static ProductTranslation Create(Guid productId, string lang, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Məhsul adı boş ola bilməz.", nameof(name));
        if (lang is not ("az" or "ru" or "en"))
            throw new ArgumentException("Dil az/ru/en ola bilər.", nameof(lang));

        return new ProductTranslation
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Lang = lang,
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    public void Update(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Məhsul adı boş ola bilməz.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}

/// <summary>Texniki spesifikasiya sətri: "Ölçü" → "190×60 sm". Sərbəst açar/dəyər —
/// kateqoriya şablonları F2-də bunun üstünə gələcək.</summary>
public sealed class ProductSpec
{
    private ProductSpec() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string Label { get; private set; } = null!;
    public string Value { get; private set; } = null!;
    public int SortOrder { get; private set; }

    public static ProductSpec Create(Guid productId, string label, string value, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Spesifikasiya adı və dəyəri boş ola bilməz.");

        return new ProductSpec
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            Label = label.Trim(),
            Value = value.Trim(),
            SortOrder = sortOrder,
        };
    }
}

/// <summary>
/// Məhsul şəkli. Faylın özü storage-dadır (lokal disk / R2); burada yalnız yol prefiksi
/// və mövcud ölçülər saxlanılır — URL-lər bunlardan qurulur.
/// </summary>
public sealed class ProductImage
{
    private ProductImage() { } // EF Core

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Storage daxilində qovluq: "products/{productId}/{imageId}".</summary>
    public string StoragePrefix { get; private set; } = null!;

    /// <summary>Mövcud variant enləri, vergüllə: "320,640,1280". srcset bunlardan qurulur.</summary>
    public string Widths { get; private set; } = null!;

    public string? AltText { get; private set; }
    public int SortOrder { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Id kənardan verilir — storage prefiksi ("products/{productId}/{id}")
    /// yaradılmadan əvvəl məlum olmalıdır. Açar boş ola bilməz: model onu bazada
    /// generasiya etmir (bax AppDbContext.DeclareClientAssignedKeys), ona görə boş Guid
    /// sıfırlarla dolu sətir kimi yazılar və ikinci şəkildə açar toqquşması verər.</summary>
    public static ProductImage Create(Guid id, Guid productId, string storagePrefix, IEnumerable<int> widths)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Şəkil Id-si boş ola bilməz.", nameof(id));

        return new ProductImage
        {
            Id = id,
            ProductId = productId,
            StoragePrefix = storagePrefix,
            Widths = string.Join(',', widths),
            SortOrder = int.MaxValue, // sona düşür; reorder dəqiqləşdirir
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public void SetAltText(string? altText) =>
        AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public IReadOnlyList<int> WidthList() =>
        Widths.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}
