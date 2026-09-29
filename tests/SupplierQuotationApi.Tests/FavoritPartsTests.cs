using System.Net;
using SupplierQuotationApi.Providers.FavoritParts;

namespace SupplierQuotationApi.Tests;

public class FavoritPartsTests
{
    // 2026-09-29 22:00 UTC = 30.09 01:00 по Москве.
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 22, 0, 0, TimeSpan.Zero);
    private const string GoodsId = "A34B264A-E029-4FDE-BB46-51ED07F48730";
    private const string WarehouseId = "71EDEB25-5659-11F0-A7F5-84FE3E0FDBAD";

    private static FavoritPartsGoods Goods(string number = "SF-2364", string brand = "Zekkert", decimal stock = 2,
        decimal? price = 2040, string? shipment = "2026-10-06T13:10:00+03:00", List<FavoritPartsGoods>? analogues = null) => new()
    {
        GoodsId = GoodsId, Number = number, Brand = brand, Name = "Пружина", Rate = 2,
        Warehouses = [new() { Id = WarehouseId, Code = "МСК РС", Price = price, Stock = stock,
            ShipmentDate = shipment is null ? null : DateTimeOffset.Parse(shipment) }],
        Analogues = analogues
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(FavoritPartsMapper.Map([Goods()], "SF2364", "Zekkert", false, Now));

        Assert.Equal($"{GoodsId}|{WarehouseId}", offer.OfferId);
        Assert.Equal("МСК РС", offer.Warehouse);
        Assert.Equal(2040m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(2, offer.MinOrderQuantity);
        Assert.Equal(6, offer.DeliveryDaysMin); // 06.10 − 30.10 по Москве
    }

    [Fact]
    public void DeliveryDays_CountsFromMoscowDate_NotUtc()
    {
        // В UTC это ещё 29.09, но по Москве уже 30.09: отгрузка 01.10 = 1 день, а не 2.
        Assert.Equal(1, FavoritPartsMapper.DeliveryDays(DateTimeOffset.Parse("2026-10-01T10:00:00+03:00"), Now));
        Assert.Equal(0, FavoritPartsMapper.DeliveryDays(null, Now));
        Assert.Equal(0, FavoritPartsMapper.DeliveryDays(DateTimeOffset.Parse("2026-09-20T10:00:00+03:00"), Now));
    }

    [Fact]
    public void Map_SkipsZeroStock_NoPrice_AndOtherArticles()
    {
        Assert.Empty(FavoritPartsMapper.Map([Goods(stock: 0)], "SF2364", null, false, Now));
        Assert.Empty(FavoritPartsMapper.Map([Goods(price: null)], "SF2364", null, false, Now));
        Assert.Empty(FavoritPartsMapper.Map([Goods(number: "OTHER-1")], "SF2364", null, false, Now));
    }

    [Fact]
    public void Map_MatchesArticleIgnoringSeparators_AndFiltersBrand()
    {
        Assert.Single(FavoritPartsMapper.Map([Goods(number: "7160-500 039 S")], "7160500039s", null, false, Now));
        Assert.Empty(FavoritPartsMapper.Map([Goods(brand: "Febi")], "SF2364", "Zekkert", false, Now));
    }

    [Fact]
    public void Map_Analogues_OnlyWhenRequested_AndMarked()
    {
        var analogue = Goods(number: "AN-1", brand: "Other");
        var goods = Goods(analogues: [analogue]);

        Assert.Single(FavoritPartsMapper.Map([goods], "SF2364", "Zekkert", false, Now));

        var withAnalogues = FavoritPartsMapper.Map([goods], "SF2364", "Zekkert", true, Now);
        Assert.Equal(2, withAnalogues.Count);
        Assert.Equal("true", withAnalogues.Single(x => x.Article == "AN-1").ProviderData!["isAnalog"]);
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private static (FavoritPartsClient, StubHandler) Client(string body)
    {
        var handler = new StubHandler(body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.favorit-parts.ru/") };
        return (new FavoritPartsClient(http, new FavoritPartsOptions { ClientKey = "ck", DeveloperKey = "dk" }), handler);
    }

    [Fact]
    public async Task Client_SendsKeysAndBrand_ParsesGoods()
    {
        var (client, handler) = Client("""{"goods":[{"goodsID":"A34B264A-E029-4FDE-BB46-51ED07F48730","number":"SF-2364","warehouses":[]}]}""");

        var goods = await client.SearchAsync("SF2364", "Zekkert", true, default);

        Assert.Single(goods);
        Assert.Contains("key=ck", handler.LastUri);
        Assert.Contains("developerKey=dk", handler.LastUri);
        Assert.Contains("brand=Zekkert", handler.LastUri);
        Assert.Contains("analogues=on", handler.LastUri);
    }

    [Fact]
    public async Task Client_ErrorBody_Throws()
    {
        var (client, _) = Client("""{"error":"Неверный ключ"}""");

        var ex = await Assert.ThrowsAsync<FavoritPartsApiException>(() => client.SearchAsync("X", null, false, default));
        Assert.Equal("Неверный ключ", ex.Message);
    }
}
