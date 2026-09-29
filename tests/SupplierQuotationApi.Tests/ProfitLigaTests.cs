using System.Net;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers.ProfitLiga;

namespace SupplierQuotationApi.Tests;

public class ProfitLigaTests
{
    private static ProfitLigaOffer Offer(string? key = "hash1", decimal? price = 11.29m, decimal? quantity = 25, int? hours = 0,
        string warehouse = "Ростов 1 (Батайск)", bool allowReturn = true, int? multi = 25) => new(
        key, "14269", "951", "Номенклатура#b09", "110906", "ELRING", "Прокладка 12 X 17", multi, quantity, price, warehouse,
        hours, "2026-09-29 14:30:00", allowReturn, 0, 99.9m, 0, null, false);

    private static ProfitLigaCard Card(string article = "110906", string brand = "ELRING", params ProfitLigaOffer[] offers) =>
        new(article, brand, "Прокладка 12 X 17 X 1/5 DIN 7603 / CU A медная", offers.Length == 0 ? [Offer()] : offers);

    [Fact]
    public void MapOriginals_FillsUnifiedOffer()
    {
        var offer = Assert.Single(ProfitLigaMapper.MapOriginals([Card()], "110906", "Elring"));

        Assert.Equal("hash1", offer.OfferId);
        Assert.Equal("Ростов 1 (Батайск)", offer.Warehouse);
        Assert.Equal(11.29m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(25, offer.Stock);
        Assert.Equal(25, offer.MinOrderQuantity);
        Assert.Equal("Прокладка 12 X 17 X 1/5 DIN 7603 / CU A медная", offer.ProductName);   // описание карточки, не прайса
        Assert.Equal("Возврат разрешён", offer.PriceNote);
        Assert.Equal("14269", offer.ProviderData!["articleId"]);
        Assert.Equal("951", offer.ProviderData["warehouseId"]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(null, 0)]
    public void DeliveryDays_RoundsHoursUp(int? hours, int expected) =>
        Assert.Equal(expected, ProfitLigaMapper.DeliveryDays(hours));

    [Fact]
    public void MapOriginals_SkipsOtherArticlesAndBrands_AndBadPrices()
    {
        var cards = new[]
        {
            Card(),
            Card(article: "OTHER-1"),
            Card(brand: "MANN"),
            Card(offers: [Offer(price: 0), Offer(key: "k2", price: null)])
        };

        var result = ProfitLigaMapper.MapOriginals(cards, "110-906", "Elring");

        Assert.Equal(["hash1"], result.Select(x => x.OfferId));
    }

    [Fact]
    public void MapOriginals_WithoutBrand_TakesAllBrands_AndDedupes()
    {
        var cards = new[] { Card(), Card(brand: "MANN", offers: Offer(key: "k2")), Card(offers: Offer()) };

        Assert.Equal(["hash1", "k2"], ProfitLigaMapper.MapOriginals(cards, "110906", null).Select(x => x.OfferId));
    }

    [Fact]
    public void MapOriginals_UsesAliases()
    {
        var aliases = ProducerAliasMap.Build([("KAYABA", "KYB")]);
        var cards = new[] { Card(article: "RA5442", brand: "KYB") };

        Assert.Empty(ProfitLigaMapper.MapOriginals(cards, "RA5442", "Kayaba"));
        Assert.Single(ProfitLigaMapper.MapOriginals(cards, "RA5442", "Kayaba", aliases));
    }

    [Fact]
    public void MapOriginals_NoHash_FallsBackToArticleWarehouseKey_AndNoReturnNote()
    {
        var offer = Assert.Single(ProfitLigaMapper.MapOriginals([Card(offers: Offer(key: null, allowReturn: false))], "110906", null));

        Assert.Equal("14269|951", offer.OfferId);
        Assert.Equal("Возврат не разрешён", offer.PriceNote);
    }

    [Theory]
    [InlineData("Переходник 1/4&quot;, 50мм", "Переходник 1/4\", 50мм")]
    [InlineData("Переходник 1 4&amp;quot;, 50мм", "Переходник 1 4\", 50мм")]     // двойное экранирование
    [InlineData(null, null)]
    public void DecodeHtml_HandlesDoubleEscaping(string? raw, string? expected) =>
        Assert.Equal(expected, ProfitLigaMapper.DecodeHtml(raw));

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (ProfitLigaClient, StubHandler) Client(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(body, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.pr-lg.ru/") };
        return (new ProfitLigaClient(http, Options.Create(new ProfitLigaOptions { Secret = "s3cret" })), handler);
    }

    private const string CardJson = """
        [{"article":"110906","brand":"ELRING","description":"Прокладка",
          "products":{"97d6c7":{"article_id":"14269","warehouse_id":"951","multi":25,"quantity":"25>","price":11.29,
                                "custom_warehouse_name":"Ростов 1","delivery_time":30,"allow_return":"1","original":"0","delivery_probability":99.9}}}]
        """;

    [Fact]
    public async Task Client_ParsesPhpStyleObjects_AndMixedTypes()
    {
        var (client, handler) = Client(CardJson);

        var cards = await client.SearchCrossesAsync("110906", "Elring", default);

        var offer = Assert.Single(Assert.Single(cards).Offers);
        Assert.Equal("97d6c7", offer.Key);          // ключ словаря — хеш предложения
        Assert.Equal(25m, offer.Quantity);            // «25>» → 25
        Assert.Equal(30, offer.DeliveryHours);
        Assert.True(offer.AllowReturn);               // "1"
        Assert.Contains("replaces=0", handler.LastUri);
        Assert.Contains("brand=Elring", handler.LastUri);
        Assert.Contains("secret=s3cret", handler.LastUri);
    }

    [Fact]
    public async Task Client_EmptyCollections_AreArrays_NotErrors()
    {
        var (client, _) = Client("""[{"article":"K1223A","brand":"FILTRON","products":[]}]""");
        var card = Assert.Single(await client.SearchItemsAsync("K1223A", default));
        Assert.Empty(card.Offers);

        var (empty, _) = Client("[]");
        Assert.Empty(await empty.SearchItemsAsync("X", default));
    }

    [Fact]
    public async Task Client_StatusError_WithHttp200_Throws()
    {
        var (client, _) = Client("""{"status":"error","message":"Неверный ключ"}""");

        var ex = await Assert.ThrowsAsync<ProfitLigaApiException>(() => client.SearchItemsAsync("X", default));
        Assert.Contains("Неверный ключ", ex.Message);
    }

    [Fact]
    public async Task Client_Http401_Throws()
    {
        var (client, _) = Client("{}", HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<ProfitLigaApiException>(() => client.SearchItemsAsync("X", default));
        Assert.Contains("401", ex.Message);
    }
}
