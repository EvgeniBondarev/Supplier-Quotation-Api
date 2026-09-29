using System.Net;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Providers.Avd;

namespace SupplierQuotationApi.Tests;

public class AvdTests
{
    private static AvdOffer Offer(string hash = "H1", decimal? price = 837, string? supplier = "LAA", string? region = "Москва",
        string? dealerStore = null, int? period = 0, bool? original = true, string number = "K1223A") => new()
    {
        Hash = hash, Price = price, Quantity = 301, Multiply = 1, SupplierPeriod = period, SupplierName = supplier,
        SupplierRegion = region, DealerStore = dealerStore, CatalogName = "FILTRON", ItemName = "Фильтр салона",
        ItemNumber = number, IsOriginal = original, SupplierInfo = "Отгрузка день в день", SupplierReturnDescription = "Возврат 3 дня"
    };

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(AvdMapper.Map([Offer()]));

        Assert.Equal("H1", offer.OfferId);
        Assert.Equal("LAA (Москва)", offer.Warehouse);
        Assert.Equal(837m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(301, offer.Stock);
        Assert.Equal(0, offer.DeliveryDaysMin);
        Assert.Equal("Отгрузка день в день Возврат 3 дня", offer.PriceNote);
        Assert.Equal("LAA", offer.ProviderData!["supplierName"]);
    }

    [Fact]
    public void Map_DedupesByHash_AndSkipsNoPrice()
    {
        var result = AvdMapper.Map([Offer("A"), Offer("A"), Offer("B"), Offer("C", price: null)]);

        Assert.Equal(["A", "B"], result.Select(x => x.OfferId));
    }

    [Fact]
    public void Map_Warehouse_PrefersDealerStore_AndHandlesMissingRegion()
    {
        Assert.Equal("Склад-1 (Москва)", AvdMapper.Map([Offer(dealerStore: "Склад-1")])[0].Warehouse);
        Assert.Equal("LAA", AvdMapper.Map([Offer(region: null)])[0].Warehouse);
    }

    [Theory]
    [InlineData("Filtron", new[] { "FILTRON" })]
    [InlineData("Febi Bilstein", new[] { "FEBI" })]
    [InlineData("Zekkert", new string[0])]     // нет совпадения — пусто, а не первый каталог
    public void SelectCatalogs_MatchesBrand(string brand, string[] expected)
    {
        var catalogs = new[] { "FILTRON", "FEBI", "PRC" };

        Assert.Equal(expected, AvdProvider.SelectCatalogs(catalogs, brand).Intersect(expected).ToArray());
        if (expected.Length == 0) Assert.Empty(AvdProvider.SelectCatalogs(catalogs, brand));
    }

    [Fact]
    public void SelectCatalogs_WithoutBrand_TakesAllUpToLimit() =>
        Assert.Equal(6, AvdProvider.SelectCatalogs(Enumerable.Range(1, 10).Select(i => $"B{i}").ToList(), null).Count);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public string? LastBody;
        public string? LastAction;
        public string? LastUri;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastUri = request.RequestUri!.ToString();
            LastAction = request.Headers.GetValues("SOAPAction").Single();
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static (AvdClient, StubHandler) Client(string body)
    {
        var handler = new StubHandler(body);
        var options = Options.Create(new AvdOptions { BaseUrl = "https://ws1.avdmotors.ru/AvdUserService.svc/secure", Login = "l", Password = "p" });
        return (new AvdClient(new HttpClient(handler), options), handler);
    }

    private const string Prices = """
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><GetOriginalPriceResponse xmlns="http://tempuri.org/">
        <GetOriginalPriceResult xmlns:a="http://schemas.datacontract.org/2004/07/Avd.Service.Models" xmlns:i="http://www.w3.org/2001/XMLSchema-instance">
        <a:PriceItems><a:PriceItem3><a:CatalogName>FILTRON</a:CatalogName><a:DealerStore i:nil="true"/><a:Hash>H1</a:Hash><a:IsOriginal>true</a:IsOriginal>
        <a:ItemNumber>K1223A</a:ItemNumber><a:Price>837</a:Price><a:Quantity>301</a:Quantity><a:SupplierName>LAA</a:SupplierName><a:SupplierPeriod>0</a:SupplierPeriod></a:PriceItem3></a:PriceItems>
        </GetOriginalPriceResult></GetOriginalPriceResponse></s:Body></s:Envelope>
        """;

    [Fact]
    public async Task Client_SendsSoapWithCredentialsInBodyOnly_AndParsesOffers()
    {
        var (client, handler) = Client(Prices);

        var offers = await client.GetOriginalPriceAsync("K1223A", "FILTRON", default);

        var offer = Assert.Single(offers);
        Assert.Equal(837m, offer.Price);
        Assert.Equal("LAA", offer.SupplierName);
        Assert.Null(offer.DealerStore);                                   // i:nil → null
        Assert.Equal("http://tempuri.org/IAvdUserService/GetOriginalPrice", handler.LastAction);
        Assert.Equal("https://ws1.avdmotors.ru/AvdUserService.svc/secure", handler.LastUri); // без завершающего слэша
        Assert.Contains("<login>l</login>", handler.LastBody);
        Assert.DoesNotContain("password", handler.LastUri);
    }

    [Fact]
    public async Task Client_SoapFault_Throws()
    {
        var (client, _) = Client("""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultstring>Неверный пароль</faultstring></s:Fault></s:Body></s:Envelope>""");

        var ex = await Assert.ThrowsAsync<AvdApiException>(() => client.GetCatalogsAsync("X", default));
        Assert.Contains("Неверный пароль", ex.Message);
    }
}
