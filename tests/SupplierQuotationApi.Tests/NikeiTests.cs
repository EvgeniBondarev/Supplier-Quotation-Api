using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers.Nikei;

namespace SupplierQuotationApi.Tests;

public class NikeiTests
{
    private static NikeiPart Part(string key = "k1", string article = "K 1223A", string brand = "FILTRON", decimal? price = 2117.98m,
        int? stock = 486, int min = 22, int max = 22, bool? returnable = false) => new()
    {
        ProductKey = key, Article = article, Brand = brand, Name = "Filter, interior air", Price = price, Stock = stock,
        Multiplicity = 1, DeliveryTime = new NikeiDelivery { Min = min, Max = max }, Percent = 96, Warehouse = "Европа 1190",
        Returnable = returnable, ReturnCost = 0, ToCart = "tok"
    };

    [Fact]
    public void MapOriginals_FillsUnifiedOffer_AndMatchesArticleWithSpaces()
    {
        var offer = Assert.Single(NikeiMapper.MapOriginals([Part()], "K1223A", "Filtron"));

        Assert.Equal("k1", offer.OfferId);
        Assert.Equal("K 1223A", offer.Article);          // артикул поставщика, как есть
        Assert.Equal("Европа 1190", offer.Warehouse);
        Assert.Equal(2117.98m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(486, offer.Stock);
        Assert.Equal(22, offer.DeliveryDaysMin);
        Assert.Equal("Процент выдачи: 96%. Возврат не предусмотрен", offer.PriceNote);
        Assert.Equal("tok", offer.ProviderData!["toCart"]);
    }

    [Fact]
    public void MapOriginals_DedupesByProductKey_AndSkipsBadRows()
    {
        var parts = new[]
        {
            Part("a"), Part("a"), Part("b", price: 0), Part("c", article: "OTHER"), Part("d", brand: "MANN"), Part("e", price: null)
        };

        Assert.Equal(["a"], NikeiMapper.MapOriginals(parts, "K1223A", "Filtron").Select(x => x.OfferId));
    }

    [Fact]
    public void MapOriginals_UnknownStock_IsNull_AndDeliveryMaxNeverBelowMin()
    {
        var offer = Assert.Single(NikeiMapper.MapOriginals([Part(stock: -1, min: 5, max: 2)], "K1223A", null));

        Assert.Null(offer.Stock);
        Assert.Equal(5, offer.DeliveryDaysMax);
    }

    [Fact]
    public void MapOriginals_UsesAliases()
    {
        var aliases = ProducerAliasMap.Build([("KAYABA", "KYB")]);
        var parts = new[] { Part(article: "RA5442", brand: "KYB") };

        Assert.Empty(NikeiMapper.MapOriginals(parts, "RA5442", "Kayaba"));
        Assert.Single(NikeiMapper.MapOriginals(parts, "RA5442", "Kayaba", aliases));
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public readonly List<string> Calls = [];
        public string? Auth;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(request.RequestUri!.PathAndQuery);
            Auth = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request)) });
        }
    }

    private static NikeiClient Client(ScriptedHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://nikei.ru/") },
            Options.Create(new NikeiOptions { Login = "lg", Password = "pw" }));

    private const string PartsJson = """{"parts":[{"product_key":"k1","article":"K 1223A","brand":"FILTRON","price":2117.98,"stock":486,"multiplicity":1,"deliverytime":{"min":22,"max":22},"warehouse":"Европа 1190","returnable":false,"to_cart":"tok"}]}""";

    [Fact]
    public async Task Client_SendsBasicAuthInHeader_AndEscapesPathSegments()
    {
        var handler = new ScriptedHandler(_ => PartsJson);
        var parts = await Client(handler).SearchAsync("K 1223A", "FEBI BILSTEIN/x", default);

        Assert.Single(parts);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("lg:pw")), handler.Auth);
        Assert.Equal("/parts/search/K%201223A/FEBI%20BILSTEIN%2Fx?filterByArticle", handler.Calls[0]);
    }

    [Fact]
    public async Task Client_GroupsResponse_ReturnsBrands_AndBusinessErrorThrows()
    {
        var groups = new ScriptedHandler(_ => """{"groups":[{"brand":"FILTRON","name":"Фильтр"},{"brand":"MANN"}]}""");
        Assert.Equal(["FILTRON", "MANN"], await Client(groups).GetBrandsAsync("K1223A", default));

        var failing = new ScriptedHandler(_ => """{"status":"error","message":"Нет доступа"}""");
        var ex = await Assert.ThrowsAsync<NikeiApiException>(() => Client(failing).GetBrandsAsync("X", default));
        Assert.Contains("Нет доступа", ex.Message);
    }

    // ---- провайдер: прямой поиск по бренду и запасной путь через список брендов ----

    private sealed class FixedAliases(ProducerAliasMap map) : IProducerAliasService
    {
        public Task<ProducerAliasMap> GetMapAsync(CancellationToken ct) => Task.FromResult(map);
    }

    private static (NikeiProvider, ScriptedHandler) Provider(Func<HttpRequestMessage, string> respond, ProducerAliasMap? map = null)
    {
        var handler = new ScriptedHandler(respond);
        var options = Options.Create(new NikeiOptions { Login = "lg", Password = "pw" });
        var services = new ServiceCollection()
            .AddSingleton(_ => new NikeiClient(new HttpClient(handler) { BaseAddress = new Uri("https://nikei.ru/") }, options))
            .BuildServiceProvider();
        return (new NikeiProvider(services, options, new FixedAliases(map ?? ProducerAliasMap.Empty)), handler);
    }

    [Fact]
    public async Task Provider_BrandGiven_SearchesDirectly_WithoutBrandsLookup()
    {
        var (provider, handler) = Provider(_ => PartsJson);

        var offers = await provider.SearchAsync(new QuotationSearch("K1223A", "Filtron", false), default);

        Assert.Single(offers);
        Assert.Single(handler.Calls);
        Assert.Contains("/Filtron?", handler.Calls[0]);
    }

    [Fact]
    public async Task Provider_AliasVariant_FindsKybForKayabaInOneStep()
    {
        var aliases = ProducerAliasMap.Build([("KAYABA", "KYB")]);
        var (provider, handler) = Provider(r => r.RequestUri!.AbsolutePath.EndsWith("/KYB")
            ? """{"parts":[{"product_key":"z","article":"RA5442","brand":"KYB","price":4692.66,"stock":2}]}"""
            : """{"parts":[]}""", aliases);

        var offers = await provider.SearchAsync(new QuotationSearch("RA5442", "Kayaba", false), default);

        Assert.Equal("z", Assert.Single(offers).OfferId);
        Assert.DoesNotContain(handler.Calls, c => c == "/parts/search/RA5442?filterByArticle");   // список брендов не понадобился
    }

    [Fact]
    public async Task Provider_DirectEmpty_FallsBackToBrandsList_AndSkipsOtherBrands()
    {
        // Прямой поиск «Febi» пуст, но у Nikei номер числится как «FEBI BILSTEIN»: находим его через список брендов.
        var (provider, handler) = Provider(r => r.RequestUri!.AbsolutePath switch
        {
            "/parts/search/34053" => """{"groups":[{"brand":"FEBI BILSTEIN"},{"brand":"MANN"}]}""",
            "/parts/search/34053/FEBI%20BILSTEIN" => """{"parts":[{"product_key":"f","article":"34053","brand":"FEBI BILSTEIN","price":1943.87,"stock":1}]}""",
            _ => """{"parts":[]}"""
        });

        var offers = await provider.SearchAsync(new QuotationSearch("34053", "Febi", false), default);

        Assert.Equal("f", Assert.Single(offers).OfferId);
        Assert.Contains("/parts/search/34053?filterByArticle", handler.Calls);
        Assert.DoesNotContain(handler.Calls, c => c.Contains("/MANN"));    // чужой бренд не запрашивался
    }

    [Fact]
    public async Task Provider_NoBrand_UsesBrandsList()
    {
        var (provider, handler) = Provider(r => r.RequestUri!.AbsolutePath.EndsWith("/K1223A")
            ? """{"groups":[{"brand":"FILTRON"}]}"""
            : PartsJson);

        var offers = await provider.SearchAsync(new QuotationSearch("K1223A", null, false), default);

        Assert.Single(offers);
        Assert.Equal(2, handler.Calls.Count);
    }
}
