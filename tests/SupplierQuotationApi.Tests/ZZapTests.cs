using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers;
using SupplierQuotationApi.Providers.ZZap;

namespace SupplierQuotationApi.Tests;

public class ZZapTests
{
    private static ZZapOffer Offer(string code = "1", string part = "K1223A", decimal? price = 839m, int? qty = 2, int? days = 1,
        int? pack = 1, string seller = "ИП Левков", string? location = "Королев", bool used = false, int? qtyMax = 2,
        decimal? minSum = 0, string? typePrice = "RDSMPJ", string? typePriceText = "") => new()
    {
        CodeDocB = code, PartNumber = part, Brand = "FILTRON", Name = "Фильтр салона", Seller = seller, Location = location,
        Price = price, Quantity = qty, QuantityText = "2 шт.", DeliveryDays = days, DeliveryText = "1 день", Pack = pack,
        QuantityMax = qtyMax, MinSumOrder = minSum, Used = used, TypePrice = typePrice, TypePriceText = typePriceText,
        Apply = "Подтверждайте резерв", Shipment = "Самовывоз", Courier = true, PriceAge = "1ч. назад", Rating = 4, RatingCount = "4 867 отзывов"
    };

    [Fact]
    public void MapOriginals_FillsUnifiedOffer_WithAttribution()
    {
        var offer = Assert.Single(ZZapMapper.MapOriginals([Offer()], "K1223A", 1));

        Assert.Equal("1", offer.OfferId);
        Assert.Equal("ИП Левков · Королев", offer.Warehouse);     // продавец и точка выдачи, а не склад
        Assert.Equal(839m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(2, offer.Stock);
        Assert.Equal(1, offer.DeliveryDaysMin);
        Assert.EndsWith(ZZapMapper.Attribution, offer.PriceNote);   // ZZap требует указывать источник
        Assert.Contains("Самовывоз", offer.PriceNote);
        Assert.Equal(ZZapMapper.Attribution, offer.ProviderData!["attribution"]);
        Assert.Equal("1", offer.ProviderData["codeRegion"]);
        Assert.Equal("4", offer.ProviderData["sellerRating"]);
    }

    [Fact]
    public void MapOriginals_SkipsUsedUnknownDeliveryOtherPartsAndBadRows()
    {
        var offers = new[]
        {
            Offer("ok"),
            Offer("used", used: true),
            Offer("nodays", days: 254),
            Offer("other", part: "OTHER"),
            Offer("noprice", price: null),
            Offer("", price: 10)
        };

        Assert.Equal(["ok"], ZZapMapper.MapOriginals(offers, "K1223A", 1).Select(x => x.OfferId));
    }

    [Fact]
    public void MapOriginals_MatchesPartNumberWithSpaces()
    {
        Assert.Single(ZZapMapper.MapOriginals([Offer(part: "OC 90")], "OC90", 1));
    }

    [Fact]
    public void MapOriginals_HiddenStock_IsNullWithText()
    {
        var offer = Assert.Single(ZZapMapper.MapOriginals([Offer(qty: -1)], "K1223A", 1));

        Assert.Null(offer.Stock);
        Assert.Equal("2 шт.", offer.StockText);
    }

    [Fact]
    public void MapOriginals_SortsByPriceThenDelivery_AndCapsAt20()
    {
        var offers = Enumerable.Range(1, 30).Select(i => Offer(code: $"c{i}", price: 1000 - i)).ToList();

        var result = ZZapMapper.MapOriginals(offers, "K1223A", 1);

        Assert.Equal(ZZapMapper.MaxOffers, result.Count);
        Assert.Equal(970m, result[0].Price.Amount);
        Assert.True(result.Zip(result.Skip(1)).All(p => p.First.Price.Amount <= p.Second.Price.Amount));
    }

    [Fact]
    public void MapOriginals_ConditionsFromSellerLimits()
    {
        var offer = Assert.Single(ZZapMapper.MapOriginals(
            [Offer(pack: 4, qtyMax: 999_999_999, minSum: 3000, typePrice: "W", typePriceText: "Цена для юр. лиц")], "K1223A", 1));

        Assert.Contains("Кратность упаковки: 4", offer.PriceNote);
        Assert.Contains("Минимальная сумма заказа у продавца: 3000", offer.PriceNote);
        Assert.DoesNotContain("Максимум к заказу", offer.PriceNote);   // 999999999 — «без ограничения»
        Assert.Equal(4, offer.MinOrderQuantity);
        Assert.Equal("true", offer.ProviderData!["legalOnly"]);
        Assert.Null(offer.ProviderData["qtyMax"]);
    }

    private sealed class ScriptedHandler(Func<int, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public int Calls;
        public string? LastUri;
        public string? ApiKey;
        public readonly List<long> CallTimes = [];
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            CallTimes.Add(_clock.ElapsedMilliseconds);
            LastUri = request.RequestUri!.ToString();
            ApiKey = request.Headers.TryGetValues("zzap-api-key", out var v) ? v.Single() : null;
            var (status, body) = respond(Calls);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (ZZapClient, ScriptedHandler) Client(Func<int, (HttpStatusCode, string)> respond, int intervalMs = 20)
    {
        var handler = new ScriptedHandler(respond);
        var options = Options.Create(new ZZapOptions { ApiKey = "secret-key", MinRequestIntervalMs = intervalMs });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://b52-api.zzap.pro/") };
        return (new ZZapClient(http, options, new ZZapThrottle(options, TimeProvider.System)), handler);
    }

    private const string Ok = """{"result":{"data":[{"code_doc_b":"1","partnumber":"K1223A","class_man":"FILTRON","price_v2":839.0,"qty_v2":2,"delivery_days":1,"used_v2":false,"pack":1}]},"success":true,"code":200,"errors":{}}""";

    [Fact]
    public async Task Client_KeyInHeaderNotUrl_AndParsesOffers()
    {
        var (client, handler) = Client(_ => (HttpStatusCode.OK, Ok));

        var offers = await client.SearchLightAsync("K1223A", "Filtron", default);

        Assert.Equal(839m, Assert.Single(offers).Price);
        Assert.Equal("secret-key", handler.ApiKey);
        Assert.DoesNotContain("secret-key", handler.LastUri);
        Assert.Contains("type_request=5", handler.LastUri);
        Assert.Contains("class_man=Filtron", handler.LastUri);
        Assert.Contains("code_region=1", handler.LastUri);
    }

    [Fact]
    public async Task Client_429_RetriesOnce_ThenSucceeds()
    {
        var (client, handler) = Client(n => n == 1
            ? (HttpStatusCode.TooManyRequests, """{"success":false,"code":429,"errors":{"error":"Превышена максимальная частота запросов"}}""")
            : (HttpStatusCode.OK, Ok));

        Assert.Single(await client.SearchLightAsync("K1223A", "Filtron", default));
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Client_429Twice_ThrowsWithStatus429()
    {
        var (client, handler) = Client(_ => (HttpStatusCode.TooManyRequests, """{"success":false,"code":429,"errors":{"error":"Превышена максимальная частота запросов"}}"""));

        var ex = await Assert.ThrowsAsync<ZZapApiException>(() => client.SearchLightAsync("K1223A", "Filtron", default));

        Assert.Equal(429, ex.StatusCode);
        Assert.Equal(2, handler.Calls);     // не больше одного повтора
    }

    [Fact]
    public async Task Client_SuccessFalseWithHttp200_Throws()
    {
        var (client, _) = Client(_ => (HttpStatusCode.OK, """{"success":false,"code":400,"errors":{"class_man":"Не указан бренд"}}"""));

        var ex = await Assert.ThrowsAsync<ZZapApiException>(() => client.SearchLightAsync("X", "Y", default));
        Assert.Contains("Не указан бренд", ex.Message);
    }

    [Fact]
    public async Task Throttle_SpacesRequestsByMinInterval_EvenWhenConcurrent()
    {
        var (client, handler) = Client(_ => (HttpStatusCode.OK, Ok), intervalMs: 150);

        await Task.WhenAll(client.SearchLightAsync("A", "B", default), client.SearchLightAsync("C", "D", default), client.SearchLightAsync("E", "F", default));

        Assert.Equal(3, handler.Calls);
        var gaps = handler.CallTimes.Zip(handler.CallTimes.Skip(1), (a, b) => b - a).ToList();
        Assert.All(gaps, gap => Assert.True(gap >= 120, $"пауза между запросами {gap} мс меньше интервала"));
    }

    [Fact]
    public async Task Provider_NoBrand_ThrowsWithoutCallingApi()
    {
        var (client, handler) = Client(_ => (HttpStatusCode.OK, Ok));
        var options = Options.Create(new ZZapOptions { ApiKey = "k" });
        var services = new ServiceCollection().AddSingleton(client).BuildServiceProvider();
        var provider = new ZZapProvider(services, options);

        var ex = await Assert.ThrowsAsync<ZZapApiException>(() => provider.SearchAsync(new QuotationSearch("K1223A", null, false), default));

        Assert.Contains("бренд", ex.Message);
        Assert.Equal(0, handler.Calls);
    }
}
