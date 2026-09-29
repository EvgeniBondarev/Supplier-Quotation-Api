using System.Net;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers.Berg;

namespace SupplierQuotationApi.Tests;

public class BergTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static BergResource Resource(string article = "K1223A", string brand = "FILTRON", List<BergOffer>? offers = null) => new()
    {
        Id = 1263404, Article = article, Name = "Фильтр салона", Brand = new BergBrand { Name = brand },
        Offers = offers ?? [Offer()]
    };

    private static BergOffer Offer(decimal? price = 964.37m, int? assured = 1, int? average = 1, bool availableMore = false,
        bool transit = false, List<BergTimetable>? timetable = null) => new()
    {
        Warehouse = new BergWarehouse { Id = 32237, Name = "OLN" }, Price = price, AssuredPeriod = assured, AveragePeriod = average,
        Quantity = 301, AvailableMore = availableMore, IsTransit = transit, MultiplicationFactor = 1, Reliability = 98,
        AddressTimetable = timetable
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(BergMapper.Map([Resource()], "K1223A", "Filtron", false, Today));

        Assert.Equal("1263404|32237", offer.OfferId);
        Assert.Equal("OLN", offer.Warehouse);
        Assert.Equal(964.37m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(301, offer.Stock);
        Assert.Equal("98", offer.ProviderData!["reliability"]);
        Assert.Equal("1263404", offer.ProviderData["resourceId"]);
    }

    [Fact]
    public void DeliveryDays_UsesTimetableDates_WhenAddressGiven()
    {
        var slot = new BergTimetable { DeliveryFrom = "2026-10-01 10:00:00", DeliveryTo = "2026-10-03 11:00:00" };

        Assert.Equal((1, 3), BergMapper.DeliveryDays(Offer(timetable: [slot]), Today));
    }

    [Fact]
    public void DeliveryDays_FallsBackToPeriods_AndNullWhenNothing()
    {
        Assert.Equal((2, 4), BergMapper.DeliveryDays(Offer(assured: 2, average: 4), Today));
        Assert.Equal((null, null), BergMapper.DeliveryDays(Offer(assured: null, average: null), Today));
    }

    [Fact]
    public void ParseDate_HandlesSpaceSeparatedFormat()
    {
        Assert.Equal(new DateOnly(2026, 10, 1), BergMapper.ParseDate("2026-10-01 10:00:00"));
        Assert.Null(BergMapper.ParseDate("bad"));
        Assert.Null(BergMapper.ParseDate(null));
    }

    [Fact]
    public void Map_FiltersBrandLocally_IncludingTransliteration()
    {
        var resources = new[] { Resource(brand: "FILTRON"), Resource(brand: "ЛУКОЙЛ") };

        Assert.Single(BergMapper.Map(resources, "K1223A", "Filtron", false, Today));
        Assert.Single(BergMapper.Map(resources, "K1223A", "LUKOIL", false, Today));
        Assert.Empty(BergMapper.Map(resources, "K1223A", "Zekkert", false, Today));
        Assert.Equal(2, BergMapper.Map(resources, "K1223A", null, false, Today).Count);
    }

    [Fact]
    public void Map_Analogs_OnlyWhenRequested_AndMarked()
    {
        var resources = new[] { Resource(), Resource(article: "OTHER-1", brand: "MANN") };

        Assert.Single(BergMapper.Map(resources, "K1223A", null, false, Today));
        var withAnalogs = BergMapper.Map(resources, "K1223A", "Filtron", true, Today);
        Assert.Equal("true", withAnalogs.Single(x => x.Article == "OTHER-1").ProviderData!["isAnalog"]);
    }

    [Fact]
    public void Map_StockText_ShowsAtLeast_AndTransitNote()
    {
        var offer = Assert.Single(BergMapper.Map([Resource(offers: [Offer(availableMore: true, transit: true)])], "K1223A", null, false, Today));

        Assert.Equal(">301", offer.StockText);
        Assert.Equal("Транзит", offer.PriceNote);
    }

    [Fact]
    public void Map_SkipsOffersWithoutPrice() =>
        Assert.Empty(BergMapper.Map([Resource(offers: [Offer(price: null)])], "K1223A", null, false, Today));

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastUri;
        public string? LastKey;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            LastKey = request.Headers.TryGetValues("X-Berg-API-Key", out var v) ? v.Single() : null;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static (BergClient, StubHandler) Client(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.berg.ru/") };
        return (new BergClient(http, Options.Create(new BergOptions { ApiKey = "secret-key" })), handler);
    }

    [Fact]
    public async Task Client_KeyInHeaderNotUrl_AndParsesStock()
    {
        var (client, handler) = Client(HttpStatusCode.OK,
            """{"resources":[{"id":1,"article":"K1223A","brand":{"name":"FILTRON"},"offers":[{"warehouse":{"id":5,"name":"OLN"},"price":10.5,"quantity":3,"address_timetable":[{"delivery_from":"2026-10-01 10:00:00"}]}]}]}""");

        var resources = await client.GetStockAsync("K1223A", false, 225876, default);

        Assert.Single(resources);
        Assert.Equal("secret-key", handler.LastKey);
        Assert.DoesNotContain("secret-key", handler.LastUri);
        Assert.Contains("address_id=225876", handler.LastUri);
        Assert.Contains("analogs=0", handler.LastUri);
    }

    [Fact]
    public async Task Client_ErrorsBody_WinsOverStatus()
    {
        var (client, _) = Client(HttpStatusCode.BadRequest, """{"errors":[{"code":"ERR_IP_ADDRESS_FORBIDDEN","message":"IP запрещён"}]}""");

        var ex = await Assert.ThrowsAsync<BergApiException>(() => client.GetStockAsync("X", false, null, default));
        Assert.Contains("IP запрещён", ex.Message);
    }
}
