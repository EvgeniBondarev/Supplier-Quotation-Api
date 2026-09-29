using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers;
using SupplierQuotationApi.Providers.Mikado;

namespace SupplierQuotationApi.Tests;

public class MikadoTests
{
    private static MikadoLine Line(string orderCode = "xzk-sf-2364", string price = "1215.65", string stockId = "77", string stockName = "Москва2",
        string qty = "2", string min = "2", string delay = "0", string? name = "Пружина подвески") =>
        new(orderCode, "Zekkert", name, price, stockId, stockName, qty, min, delay, null, "https://pub.fsa.gov.ru/cert/1");

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(MikadoMapper.Map([Line()], "SF2364"));

        Assert.Equal("xzk-sf-2364|77|1215.65", offer.OfferId);
        Assert.Equal("SF2364", offer.Article);             // артикул запроса: в строке только внутренний OrderCode
        Assert.Equal("Москва2", offer.Warehouse);
        Assert.Equal(1215.65m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(2, offer.Stock);
        Assert.Equal(2, offer.MinOrderQuantity);
        Assert.Equal(0, offer.DeliveryDaysMin);
        Assert.Equal("xzk-sf-2364", offer.ProviderData!["orderCode"]);
        Assert.Equal("77", offer.ProviderData["stockId"]);
        Assert.Equal("https://pub.fsa.gov.ru/cert/1", offer.ProviderData["certificateUrl"]);
    }

    [Fact]
    public void Map_DedupesSameWarehouseAndPrice_SkipsBadPrice_KeepsDifferentWarehouses()
    {
        var lines = new[] { Line(), Line(), Line(stockId: "57", stockName: "Орел"), Line(price: "abc"), Line(price: "0") };

        Assert.Equal(["77", "57"], MikadoMapper.Map(lines, "SF2364").Select(x => x.ProviderData!["stockId"]));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("2 дн.", 2)]
    [InlineData(null, 0)]
    [InlineData("нет", 0)]
    public void Days_TakesDigits(string? raw, int expected) => Assert.Equal(expected, MikadoMapper.Days(raw));

    [Fact]
    public void Map_DelayFallsBackToStockDelay()
    {
        var line = Line() with { DeliveryDelay = null, StockDelay = "3" };

        Assert.Equal(3, Assert.Single(MikadoMapper.Map([line], "X")).DeliveryDaysMin);
    }

    private sealed class StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls;
        public string? LastBody;
        public string? LastAction;
        public string? LastUri;
        public readonly List<string> Brands = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            LastAction = request.Headers.GetValues("SOAPAction").Single();
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            var brand = System.Text.RegularExpressions.Regex.Match(LastBody, "<Brand>(.*?)</Brand>").Groups[1].Value;
            Brands.Add(brand);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private const string Ok = """
        <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><CodeBrandStockInfoResponse xmlns="http://mikado-parts.ru/service"><CodeBrandStockInfoResult>
        <Code_Search>SF2364</Code_Search><Message>Ok</Message><List>
        <CodeBrandLine><OrderCode>xzk-sf-2364</OrderCode><PriceRUR>1215.65</PriceRUR><Brand>Zekkert</Brand><Name>Пружина</Name><StokID>77</StokID><StokName>Москва2</StokName><StockQTY>2</StockQTY><MinZakazQTY>2</MinZakazQTY><DeliveryDelay>0</DeliveryDelay></CodeBrandLine>
        </List><Certificate><Url>https://pub.fsa.gov.ru/cert/1</Url></Certificate></CodeBrandStockInfoResult></CodeBrandStockInfoResponse></soap:Body></soap:Envelope>
        """;

    private static (MikadoClient, StubHandler) Client(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new StubHandler(body, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.mikado-parts.ru/ws1/") };
        return (new MikadoClient(http, Options.Create(new MikadoOptions { ClientId = "10001", Password = "pw" })), handler);
    }

    [Fact]
    public async Task Client_SendsFieldsInWsdlOrder_ToServiceAsmx_AndParsesLines()
    {
        var (client, handler) = Client(Ok);

        var lines = await client.CodeBrandStockInfoAsync("SF2364", "Zekkert", default);

        var line = Assert.Single(lines);
        Assert.Equal("Москва2", line.StockName);
        Assert.Equal("https://pub.fsa.gov.ru/cert/1", line.CertificateUrl);
        Assert.Equal("https://www.mikado-parts.ru/ws1/service.asmx", handler.LastUri);
        Assert.Equal("\"http://mikado-parts.ru/service/CodeBrandStockInfo\"", handler.LastAction);
        // Порядок значим: Code, Brand, ClientID, Password (ASMX разбирает тело последовательно).
        var body = handler.LastBody!;
        Assert.True(body.IndexOf("<Code>", StringComparison.Ordinal) < body.IndexOf("<Brand>", StringComparison.Ordinal));
        Assert.True(body.IndexOf("<Brand>", StringComparison.Ordinal) < body.IndexOf("<ClientID>", StringComparison.Ordinal));
        Assert.True(body.IndexOf("<ClientID>", StringComparison.Ordinal) < body.IndexOf("<Password>", StringComparison.Ordinal));
        Assert.Contains("xmlns=\"http://mikado-parts.ru/service\"", body);
    }

    [Fact]
    public async Task Client_EscapesXmlInBrand()
    {
        var (client, handler) = Client(Ok);

        await client.CodeBrandStockInfoAsync("W28510", "WYNN'S & <Co>", default);

        Assert.Contains("<Brand>WYNN'S &amp; &lt;Co&gt;</Brand>", handler.LastBody);
    }

    [Fact]
    public async Task Client_MessageNotOk_ThrowsInsteadOfLookingEmpty()
    {
        // Неверный namespace/логин: сервис отвечает HTTP 200, пустым списком и текстом в Message.
        var (client, _) = Client("""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><R xmlns="http://mikado-parts.ru/service"><CodeBrandStockInfoResult><Message>Некорректное значение параметра [ClientID];</Message><List /></CodeBrandStockInfoResult></R></s:Body></s:Envelope>""");

        var ex = await Assert.ThrowsAsync<MikadoApiException>(() => client.CodeBrandStockInfoAsync("X", "Y", default));
        Assert.Contains("ClientID", ex.Message);
    }

    [Fact]
    public async Task Client_EmptyListWithOk_IsNoOffers_AndSoapFaultThrows()
    {
        var (empty, _) = Client("""<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><R xmlns="http://mikado-parts.ru/service"><CodeBrandStockInfoResult><Message>Ok</Message><List /></CodeBrandStockInfoResult></R></s:Body></s:Envelope>""");
        Assert.Empty(await empty.CodeBrandStockInfoAsync("K1223A", "FILTRON", default));

        var (fault, _) = Client("""<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/"><soap:Body><soap:Fault><faultstring>Server was unable to process request</faultstring></soap:Fault></soap:Body></soap:Envelope>""");
        await Assert.ThrowsAsync<MikadoApiException>(() => fault.CodeBrandStockInfoAsync("X", "Y", default));
    }

    private sealed class FixedAliases(ProducerAliasMap map) : IProducerAliasService
    {
        public Task<ProducerAliasMap> GetMapAsync(CancellationToken ct) => Task.FromResult(map);
    }

    private static (MikadoProvider, StubHandler) Provider(ProducerAliasMap? map = null)
    {
        var handler = new StubHandler(Ok);
        var options = Options.Create(new MikadoOptions { ClientId = "10001", Password = "pw" });
        var services = new ServiceCollection()
            .AddSingleton(_ => new MikadoClient(new HttpClient(handler) { BaseAddress = new Uri("https://www.mikado-parts.ru/ws1/") }, options))
            .BuildServiceProvider();
        return (new MikadoProvider(services, options, new FixedAliases(map ?? ProducerAliasMap.Empty)), handler);
    }

    [Fact]
    public async Task Provider_NoBrand_ThrowsWithoutCallingApi()
    {
        var (provider, handler) = Provider();

        var ex = await Assert.ThrowsAsync<MikadoApiException>(() => provider.SearchAsync(new QuotationSearch("SF2364", null, false), default));

        Assert.Contains("бренд", ex.Message);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Provider_TriesAliasVariants_AndMergesWithoutDuplicates()
    {
        var aliases = ProducerAliasMap.Build([("KAYABA", "KYB")]);
        var (provider, handler) = Provider(aliases);

        var offers = await provider.SearchAsync(new QuotationSearch("SF2364", "Kayaba", false), default);

        Assert.Equal(["KYB", "Kayaba"], handler.Brands.Order(StringComparer.Ordinal));   // запросы идут параллельно, порядок не важен
        Assert.Single(offers);    // обе попытки вернули одну и ту же строку — слияние убрало дубль
    }
}
