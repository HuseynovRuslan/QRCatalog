using QrCatalog.Domain.Entities;

namespace QrCatalog.UnitTests;

public class CompanyTests
{
    [Fact]
    public void Create_ValidInput_SetsFieldsAndActivates()
    {
        var company = Company.Create("  Bağ Mebeli MMC  ", "bag-mebeli");

        Assert.Equal("Bağ Mebeli MMC", company.Name);
        Assert.Equal("bag-mebeli", company.Slug);
        Assert.True(company.IsActive);
        Assert.NotEqual(Guid.Empty, company.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Company.Create(name, "slug"));
    }

    [Theory]
    [InlineData("Böyük-Hərf")]
    [InlineData("boşluq var")]
    [InlineData("-defislə-başlayır")]
    [InlineData("defislə-bitir-")]
    [InlineData("qoşa--defis")]
    [InlineData("")]
    public void Create_InvalidSlug_Throws(string slug)
    {
        Assert.Throws<ArgumentException>(() => Company.Create("Ad", slug));
    }

    [Fact]
    public void Deactivate_SetsInactive()
    {
        var company = Company.Create("Ad", "ad");
        company.Deactivate();
        Assert.False(company.IsActive);
    }
}
