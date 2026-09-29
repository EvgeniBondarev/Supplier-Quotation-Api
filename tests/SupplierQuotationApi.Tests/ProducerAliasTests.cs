using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Contracts;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers;
using SupplierQuotationApi.Providers.Avd;
using SupplierQuotationApi.Providers.Berg;
using SupplierQuotationApi.Providers.FavoritParts;
using SupplierQuotationApi.Providers.ForumAuto;
using SupplierQuotationApi.Providers.ShateM;

namespace SupplierQuotationApi.Tests;

public class ProducerAliasTests
{
    // Строки, как в OneCProducerAliases базы Studio2.
    private static readonly ProducerAliasMap Aliases = ProducerAliasMap.Build(
    [
        ("KAYABA", "KYB"), ("BLUE", "BLUE PRINT"), ("HERTHBUSSHB", "Herth+Buss"), ("A", "B"), ("B", "C")
    ]);

    [Fact]
    public void Build_MapsAliasToCanonical_AndCollapsesChains()
    {
        Assert.Equal("KYB", Aliases.Canonicalize("Kayaba"));
        Assert.Equal("BLUEPRINT", Aliases.Canonicalize("Blue"));
        Assert.Equal("C", Aliases.Canonicalize("A"));           // A → B → C
        Assert.Equal("ZEKKERT", Aliases.Canonicalize("Zekkert")); // незнакомый остаётся собой
    }

    [Fact]
    public void Build_IgnoresSelfLoopsAndEmptyRows_AndSurvivesCycles()
    {
        var map = ProducerAliasMap.Build([("X", "X"), ("", "Y"), ("Z", null), ("P", "Q"), ("Q", "P")]);

        Assert.Equal(2, map.Count);                   // остались только P и Q
        Assert.Equal("Z", map.Canonicalize("Z"));     // кольцо не зацикливает разбор
    }

    [Theory]
    [InlineData("KYB", "Kayaba", true)]
    [InlineData("Kayaba", "KYB", true)]
    [InlineData("Blue Print", "Blue", true)]
    [InlineData("MAHLE ORIGINAL", "Mahle", true)]        // вхождение сохранено
    [InlineData("Filtron", null, true)]                   // бренд не задан — подходит любой
    [InlineData("Filtron", "Mann", false)]
    [InlineData(null, "Mann", false)]
    public void Matches_UsesAliasesAndContainment(string? actual, string? requested, bool expected) =>
        Assert.Equal(expected, Aliases.Matches(actual, requested));

    [Fact]
    public void Matches_WithoutAliases_DoesNotKnowKayabaIsKyb() =>
        Assert.False(ProducerAliasMap.Empty.Matches("KYB", "Kayaba"));

    [Fact]
    public void QueryVariants_IncludesOriginalCleanedAndAliases_WithoutDuplicates()
    {
        Assert.Equal(["Kayaba", "KYB"], Aliases.QueryVariants("Kayaba"));   // KAYABA — дубль без учёта регистра
        Assert.Equal(["WYNN'S", "WYNNS"], ProducerAliasMap.Empty.QueryVariants("WYNN'S"));
        Assert.Equal(["Filtron"], ProducerAliasMap.Empty.QueryVariants("Filtron"));
        Assert.Empty(Aliases.QueryVariants("  "));
        Assert.Equal(2, Aliases.QueryVariants("Kayaba", max: 2).Count);
    }

    // ---- провайдеры используют карту ----

    [Fact]
    public void Avd_SelectCatalogs_FindsKybForKayaba()
    {
        var catalogs = new[] { "KYB", "FILTRON" };

        Assert.Empty(AvdProvider.SelectCatalogs(catalogs, "Kayaba"));
        Assert.Equal(["KYB"], AvdProvider.SelectCatalogs(catalogs, "Kayaba", Aliases));
    }

    [Fact]
    public void ShateM_MatchArticles_UsesAliases()
    {
        var found = new[]
        {
            new ShateMArticle(1, "AG1", "KYB", "Амортизатор", null),
            new ShateMArticle(2, "AG1", "FEBI", "Амортизатор", null)
        };
        var search = new QuotationSearch("AG1", "Kayaba", false);

        Assert.Empty(ShateMProvider.MatchArticles(found, search));
        Assert.Equal(1, Assert.Single(ShateMProvider.MatchArticles(found, search, Aliases)).Id);
    }

    [Fact]
    public void ForumAuto_Map_UsesAliases()
    {
        var row = new ForumAutoRow { Gid = "G", Art = "AG1", Brand = "KYB", Price = 100 };

        Assert.Empty(ForumAutoMapper.Map([row], "AG1", "Kayaba", false));
        Assert.Single(ForumAutoMapper.Map([row], "AG1", "Kayaba", false, Aliases));
    }

    [Fact]
    public void FavoritParts_Map_UsesAliases()
    {
        var goods = new FavoritPartsGoods
        {
            GoodsId = "A34B264A-E029-4FDE-BB46-51ED07F48730", Number = "AG1", Brand = "KYB",
            Warehouses = [new() { Id = "71EDEB25-5659-11F0-A7F5-84FE3E0FDBAD", Code = "МСК", Price = 10, Stock = 1 }]
        };
        var now = DateTimeOffset.UtcNow;

        Assert.Empty(FavoritPartsMapper.Map([goods], "AG1", "Kayaba", false, now));
        Assert.Single(FavoritPartsMapper.Map([goods], "AG1", "Kayaba", false, now, Aliases));
    }

    [Fact]
    public void Berg_BrandMatches_CombinesAliasesAndTransliteration()
    {
        Assert.True(BergMapper.BrandMatches("KYB", "Kayaba", Aliases));
        Assert.True(BergMapper.BrandMatches("ЛУКОЙЛ", "LUKOIL"));
        Assert.False(BergMapper.BrandMatches("KYB", "Kayaba"));
    }

    // ---- сервис: чтение БД и деградация ----

    private static ProducerAliasService Service(string? connectionString) =>
        new(Options.Create(new Studio2DbOptions { ConnectionString = connectionString }),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<ProducerAliasService>.Instance);

    [Fact]
    public async Task Service_NotConfigured_ReturnsEmptyMap()
    {
        var map = await Service(null).GetMapAsync(default);

        Assert.Same(ProducerAliasMap.Empty, map);
    }

    [Fact]
    public async Task Service_UnreachableDatabase_DegradesToEmptyMap_AndDoesNotThrow()
    {
        // Порт 1 закрыт: соединение отклоняется сразу.
        var service = Service("Server=127.0.0.1;Port=1;Database=x;User=u;Password=p;ConnectionTimeout=2;SslMode=None");

        var first = await service.GetMapAsync(default);
        var second = await service.GetMapAsync(default);

        Assert.Same(ProducerAliasMap.Empty, first);
        Assert.Same(first, second);      // сбой закэширован, повторной попытки на каждый запрос нет
    }
}
