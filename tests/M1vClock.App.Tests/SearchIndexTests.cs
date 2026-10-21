using M1vClock.App.Search;

namespace M1vClock.App.Tests;

public sealed class SearchIndexTests
{
    private static readonly string[] MemoryKeywords = ["ram", "ddr", "latencia"];
    private static readonly string[] StorageKeywords = ["disco", "ssd", "nvme"];
    private static readonly string[] StressKeywords = ["carga", "estabilidade"];
    private static readonly string[] ThemeKeywords = ["tema", "escuro"];
    private static readonly string[] StandbyKeywords = ["standby", "memoria"];

    private static readonly SearchItem[] Items =
    [
        Page("memory", "Memória", "Hardware", MemoryKeywords),
        Page("storage", "Armazenamento", "Hardware", StorageKeywords),
        Page("stress", "Teste de estresse", "Desempenho", StressKeywords),
        Action("action.theme.dark", "Usar tema escuro", ThemeKeywords),
        Page("standby", "Memória em espera", "Windows", StandbyKeywords),
    ];

    [Theory]
    [InlineData("Memória", "memoria")]
    [InlineData("AÇÃO", "acao")]
    [InlineData("Configurações", "configuracoes")]
    [InlineData("  Já  ", "  ja  ")]
    public void NormalizeRemovesAccentsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, SearchIndex.Normalize(input));
    }

    [Fact]
    public void EmptyQueryReturnsTheFirstItemsInOrder()
    {
        var result = SearchIndex.Query(Items, "   ", 3);

        Assert.Equal(["memory", "storage", "stress"], result.Select(i => i.Id));
    }

    [Fact]
    public void QueryIgnoresAccentsAndCase()
    {
        var result = SearchIndex.Query(Items, "MEMORIA");

        Assert.Contains(result, i => i.Id == "memory");
    }

    [Fact]
    public void TitlePrefixBeatsKeywordMatch()
    {
        var result = SearchIndex.Query(Items, "mem");

        Assert.Equal("memory", result[0].Id);
    }

    [Fact]
    public void PhrasePrefixRanksTheMoreSpecificTitleFirst()
    {
        var result = SearchIndex.Query(Items, "memoria em");

        Assert.Equal("standby", result[0].Id);
    }

    [Fact]
    public void KeywordFindsPagesThatTheTitleDoesNot()
    {
        var result = SearchIndex.Query(Items, "ssd");

        Assert.Equal(["storage"], result.Select(i => i.Id));
    }

    [Fact]
    public void WordInsideTheTitleMatches()
    {
        var result = SearchIndex.Query(Items, "estresse");

        Assert.Equal("stress", result[0].Id);
    }

    [Fact]
    public void EveryWordOfTheQueryMustMatch()
    {
        Assert.Single(SearchIndex.Query(Items, "teste estresse"));
        Assert.Empty(SearchIndex.Query(Items, "teste inexistente"));
    }

    [Fact]
    public void UnknownQueryReturnsNothing()
    {
        Assert.Empty(SearchIndex.Query(Items, "zzzz"));
    }

    [Fact]
    public void PagesComeBeforeActionsOnEqualScore()
    {
        var pages = new[]
        {
            Action("action.a", "Escuro", []),
            Page("page.a", "Escuro", "Hardware", []),
        };

        var result = SearchIndex.Query(pages, "escuro");

        Assert.Equal(["page.a", "action.a"], result.Select(i => i.Id));
    }

    [Fact]
    public void ResultsAreLimited()
    {
        var many = Enumerable.Range(0, 30).Select(i => Page($"p{i}", $"Página {i}", "Hardware", [])).ToList();

        Assert.Equal(5, SearchIndex.Query(many, "pagina", 5).Count);
    }

    private static SearchItem Page(string id, string title, string subtitle, string[] keywords) =>
        new(id, title, subtitle, "Icon.Overview", keywords, SearchItemKind.Page);

    private static SearchItem Action(string id, string title, string[] keywords) =>
        new(id, title, "Ação", "Icon.Sun", keywords, SearchItemKind.Action);
}
