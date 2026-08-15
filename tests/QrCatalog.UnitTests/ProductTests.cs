using QrCatalog.Domain.Entities;

namespace QrCatalog.UnitTests;

public class ProductTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid CategoryId = Guid.NewGuid();

    [Fact]
    public void Create_StartsAsDraft()
    {
        var product = Product.Create(CompanyId, CategoryId, "sezlonq-bahama", "  SKU-42  ");

        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Equal("SKU-42", product.Sku);
    }

    [Fact]
    public void StatusTransitions_Work()
    {
        var product = Product.Create(CompanyId, CategoryId, "slug", null);

        product.Publish();
        Assert.Equal(ProductStatus.Published, product.Status);

        product.Unpublish();
        Assert.Equal(ProductStatus.Draft, product.Status);

        product.Archive();
        Assert.Equal(ProductStatus.Archived, product.Status);
    }

    [Fact]
    public void Translation_RejectsUnknownLang()
    {
        Assert.Throws<ArgumentException>(() =>
            ProductTranslation.Create(Guid.NewGuid(), "tr", "Ad", null));
    }

    [Fact]
    public void Translation_RejectsEmptyName()
    {
        Assert.Throws<ArgumentException>(() =>
            ProductTranslation.Create(Guid.NewGuid(), "az", "  ", null));
    }

    [Fact]
    public void Image_WidthListRoundTrips()
    {
        var id = Guid.NewGuid();
        var image = ProductImage.Create(id, Guid.NewGuid(), $"products/x/{id}", [320, 640, 1280]);

        Assert.Equal([320, 640, 1280], image.WidthList());
    }
}
