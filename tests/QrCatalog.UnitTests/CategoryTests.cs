using QrCatalog.Domain.Entities;

namespace QrCatalog.UnitTests;

public class CategoryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var category = Category.Create(CompanyId, "  Şezlonqlar  ", "sezlonqlar",
            parentId: null, sortOrder: 3, description: " Açıq hava ", codePrefix: "SZ");

        Assert.Equal("Şezlonqlar", category.Name);
        Assert.Equal("sezlonqlar", category.Slug);
        Assert.Equal("Açıq hava", category.Description);
        Assert.Equal("SZ", category.CodePrefix);
        Assert.Equal(3, category.SortOrder);
        Assert.Equal(CompanyId, category.CompanyId);
        Assert.Null(category.ParentId);
    }

    [Fact]
    public void Create_EmptyCompany_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Category.Create(Guid.Empty, "Ad", "ad", null, 0));
    }

    [Theory]
    [InlineData("S")]      // çox qısa
    [InlineData("SEZLO")]  // çox uzun
    [InlineData("sz")]     // kiçik hərf
    [InlineData("S1")]     // rəqəm
    public void Update_InvalidCodePrefix_Throws(string prefix)
    {
        var category = Category.Create(CompanyId, "Ad", "ad", null, 0);
        Assert.Throws<ArgumentException>(() => category.Update("Ad", null, prefix));
    }

    [Fact]
    public void MoveTo_Self_Throws()
    {
        var category = Category.Create(CompanyId, "Ad", "ad", null, 0);
        Assert.Throws<ArgumentException>(() => category.MoveTo(category.Id, 0));
    }
}
