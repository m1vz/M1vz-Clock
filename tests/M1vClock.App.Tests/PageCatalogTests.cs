using M1vClock.App.Localization;
using M1vClock.App.Navigation;

namespace M1vClock.App.Tests;

public sealed class PageCatalogTests
{
    public static TheoryData<string> PageIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var page in PageCatalog.All)
            {
                data.Add(page.Id);
            }

            return data;
        }
    }

    [Fact]
    public void IdsAreUnique()
    {
        var ids = PageCatalog.All.Select(p => p.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(PageIds))]
    public void EveryPageHasTitleDescriptionAndKeywordsInThePortugueseResources(string id)
    {
        var page = PageCatalog.Find(id);

        Assert.True(Loc.Exists($"Page.{page.Key}.Title"), $"falta Page.{page.Key}.Title");
        Assert.True(Loc.Exists($"Page.{page.Key}.Description"), $"falta Page.{page.Key}.Description");
        Assert.True(Loc.Exists($"Page.{page.Key}.Keywords"), $"falta Page.{page.Key}.Keywords");
        Assert.NotEmpty(page.Keywords);
        Assert.False(string.IsNullOrWhiteSpace(page.Title));
    }

    [Fact]
    public void EverySidebarSectionHasATitle()
    {
        foreach (var section in Enum.GetValues<SidebarSection>())
        {
            Assert.True(Loc.Exists($"Section.{section}"), $"falta Section.{section}");
        }
    }

    [Theory]
    [InlineData("design-system", "DesignSystem")]
    [InlineData("maker-ports", "MakerPorts")]
    [InlineData("overview", "Overview")]
    public void KeyIsThePascalCaseOfTheId(string id, string key)
    {
        Assert.Equal(key, PageCatalog.Find(id).Key);
    }

    [Fact]
    public void FindIsCaseInsensitiveAndRejectsUnknownIds()
    {
        Assert.Equal("history", PageCatalog.Find("HISTORY").Id);
        Assert.Throws<KeyNotFoundException>(() => PageCatalog.Find("nao-existe"));
    }

    [Fact]
    public void OnlyThePhaseOnePagesAreAvailableNow()
    {
        var available = PageCatalog.All.Where(p => p.IsAvailable).Select(p => p.Id).Order().ToList();

        Assert.Equal(["design-system", "history", "settings"], available);
    }

    [Fact]
    public void FooterPagesAreTheAppSectionAndComeLast()
    {
        var last = PageCatalog.All.Skip(PageCatalog.All.Count - 3).Select(p => p.Section);

        Assert.All(last, section => Assert.Equal(SidebarSection.App, section));
        Assert.Equal(3, PageCatalog.All.Count(p => p.Section == SidebarSection.App));
    }

    [Fact]
    public void ThePhaseNumbersMatchThePlan()
    {
        Assert.All(PageCatalog.All, page => Assert.InRange(page.Phase, 1, 7));
    }
}
