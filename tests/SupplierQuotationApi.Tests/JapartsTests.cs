using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Providers.Japarts;

namespace SupplierQuotationApi.Tests;

public class JapartsTests
{
    private static JapartsOffer Offer(string priceId = "414322297537", string detail = "K1223A", string make = "FILTRON",
        decimal? price = 826.28m, decimal? quantity = 50, decimal? lot = 1, int? days = 3, int? guaranteed = 4,
        bool uncReturn = false, bool deposit = false) => new(
        priceId, make, detail, "ФИЛЬТР САЛОНА", price, quantity, lot, days, guaranteed, "Москва", "12643", 100, deposit, uncReturn, false);

    [Fact]
    public void Map_FillsUnifiedOffer()
    {
        var offer = Assert.Single(JapartsMapper.Map([Offer(uncReturn: true)], "K1223A", "Filtron"));

        Assert.Equal("414322297537", offer.OfferId);
        Assert.Equal("Москва 12643", offer.Warehouse);
        Assert.Equal(826.28m, offer.Price.Amount);
        Assert.Equal("RUB", offer.Price.Currency);
        Assert.Equal(50, offer.Stock);
        Assert.Equal(3, offer.DeliveryDaysMin);
        Assert.Equal(4, offer.DeliveryDaysMax);
        Assert.Equal("Безусловный возврат. Статистика поставщика: 100%", offer.PriceNote);
        Assert.Equal("0", offer.ProviderData!["deposit"]);
    }

    [Fact]
    public void Map_GuaranteedDays_NeverBelowNormalDays()
    {
        var offer = Assert.Single(JapartsMapper.Map([Offer(days: 5, guaranteed: 2)], "K1223A", null));

        Assert.Equal(5, offer.DeliveryDaysMax);
    }

    [Fact]
    public void Map_SkipsOtherNumbersBrandsDuplicatesAndNoPrice()
    {
        var offers = new[]
        {
            Offer("1"), Offer("1"), Offer("2", detail: "OTHER"), Offer("3", make: "MANN"), Offer("4", price: null)
        };

        Assert.Equal(["1"], JapartsMapper.Map(offers, "K-1223A", "Filtron").Select(x => x.OfferId));
    }

    [Fact]
    public void Map_MatchesCurlyApostropheBrand_AndAliases()
    {
        var curly = Offer(make: "Wynn’s", detail: "W28510");
        Assert.Single(JapartsMapper.Map([curly], "W28510", "WYNN'S"));   // «Wynn’s» и «WYNN'S» — один бренд после нормализации

        var aliases = ProducerAliasMap.Build([("KAYABA", "KYB")]);
        var kyb = Offer(make: "KYB", detail: "RA5442");
        Assert.Empty(JapartsMapper.Map([kyb], "RA5442", "Kayaba"));
        Assert.Single(JapartsMapper.Map([kyb], "RA5442", "Kayaba", aliases));
    }

    [Theory]
    [InlineData("Filtron", true)]
    [InlineData("Wynn’s", true)]      // типографский апостроф безопасен
    [InlineData("WYNN'S", false)]
    [InlineData("a\"b", false)]
    [InlineData("a;b", false)]
    [InlineData(null, true)]
    public void IsSafeParameter_RejectsSqlBreakingChars(string? value, bool expected) =>
        Assert.Equal(expected, JapartsClient.IsSafeParameter(value));

    private sealed class StubHandler(byte[] body) : HttpMessageHandler
    {
        public int Calls;
        public string? LastUri;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }

    static JapartsTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static (JapartsClient, StubHandler) Client(string json)
    {
        var handler = new StubHandler(Encoding.GetEncoding(1251).GetBytes(json));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://www.japarts.ru/") };
        return (new JapartsClient(http, Options.Create(new JapartsOptions { Login = "lg", Password = "pw" })), handler);
    }

    [Fact]
    public async Task Client_ParsesWindows1251_AndStringNumbers()
    {
        var (client, handler) = Client("""[{"priceid":"1","makename":"FILTRON","detailnum":"K1223A","detailname":"ФИЛЬТР САЛОНА","pricerur":"826.28","quantity":"50","lot":"1","time":"3","timegar":"4","country":"Москва","supcode":"12643","statistic":"100","deposit":"0","uncreturn":"1","refurbished":"0"}]""");

        var offers = await client.SearchAsync("K1223A", "Filtron", default);

        var offer = Assert.Single(offers);
        Assert.Equal("ФИЛЬТР САЛОНА", offer.DetailName);      // кириллица из windows-1251
        Assert.Equal(826.28m, offer.PriceRub);
        Assert.True(offer.UnconditionalReturn);
        Assert.Contains("cross=0", handler.LastUri);
        Assert.Contains("makename=Filtron", handler.LastUri);
    }

    [Fact]
    public async Task Client_NoResultsError_IsEmpty_OtherErrorThrows()
    {
        var (empty, _) = Client("""[{"error":"NO RESULTS FOUND"}]""");
        Assert.Empty(await empty.SearchAsync("X", null, default));

        var (failing, _) = Client("""[{"error":"WRONG LOGIN OR PASSWORD"}]""");
        var ex = await Assert.ThrowsAsync<JapartsApiException>(() => failing.SearchAsync("X", null, default));
        Assert.Contains("WRONG LOGIN", ex.Message);
    }

    [Fact]
    public async Task Client_UnsafeValues_NeverReachTheServer()
    {
        var (client, handler) = Client("[]");

        await Assert.ThrowsAsync<JapartsApiException>(() => client.SearchAsync("W'28", null, default));
        await Assert.ThrowsAsync<JapartsApiException>(() => client.SearchAsync("W28510", "WYNN'S", default));
        Assert.Equal(0, handler.Calls);
    }
}
