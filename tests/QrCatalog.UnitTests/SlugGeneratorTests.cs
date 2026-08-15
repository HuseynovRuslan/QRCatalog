using QrCatalog.Application.Common;

namespace QrCatalog.UnitTests;

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Şezlonqlar", "sezlonqlar")]
    [InlineData("Şezlonq və çətirlər", "sezlonq-ve-cetirler")]
    [InlineData("Bağ mebeli — 2026", "bag-mebeli-2026")]
    [InlineData("  Skameyka  ", "skameyka")]
    [InlineData("Ərik ağacı", "erik-agaci")]
    [InlineData("İdman qurğuları", "idman-qurgulari")]
    [InlineData("Üzgüçülük", "uzguculuk")]
    [InlineData("!!!", "kateqoriya")] // heç nə qalmırsa fallback
    public void FromName_Transliterates(string name, string expected)
    {
        Assert.Equal(expected, SlugGenerator.FromName(name));
    }
}
